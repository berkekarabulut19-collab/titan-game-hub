using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TitanGameHub
{
    public class DownloadInstallService
    {
        public event Action<string, int, string> OnProgressChanged; // (statusText, percentage, speedInfo)
        public event Action<GameProfile, bool, string> OnInstallCompleted;
        public event Action<GameProfile, SitePageAnalysis> OnSiteMirrorsDiscovered;
        public event Action<GameProfile, string, string, string> OnUpdateDetectedMidDownload; // (profile, oldVer, newVer, newUrl)

        // Kullanıcıya kurulum dizinini seçtirmek için UI tarafından doldurulur
        public static Func<GameProfile, string> InstallLocationPicker { get; set; }

        public static string CustomGamesRootDirectory { get; set; }
        public static long SpeedLimitBytesPerSec = 0; // 0 = unlimited
        public static bool AutoThrottleWhenGameRunning = false;
        public static Func<bool> CheckIsGameRunningFunc = null;

        static DownloadInstallService()
        {
            try
            {
                string[] cfgPaths = new string[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "titan_user.cfg"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "titan_user.cfg")
                };
                for (int c = 0; c < cfgPaths.Length; c++)
                {
                    if (!File.Exists(cfgPaths[c])) continue;
                    string[] lines = File.ReadAllLines(cfgPaths[c], Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string l = lines[i].Trim();
                        if (l.StartsWith("#")) continue;
                        if (l.StartsWith("MAX_DOWNLOAD_MBPS", StringComparison.OrdinalIgnoreCase))
                        {
                            int eq = l.IndexOf('=');
                            double mbps;
                            if (eq > 0 && double.TryParse(l.Substring(eq + 1).Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out mbps) && mbps > 0)
                            {
                                // Convert megabits/sec to bytes/sec
                                SpeedLimitBytesPerSec = (long)(mbps * 125000.0);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public static string GetEffectiveGamesRoot()
        {
            if (!string.IsNullOrEmpty(CustomGamesRootDirectory) && Directory.Exists(CustomGamesRootDirectory))
                return CustomGamesRootDirectory;
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Games");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }
            return dir;
        }

        private static readonly Dictionary<string, string> _targetInstallDirectories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string _cachedGoFileAccountToken = null;
        private static DateTime _cachedGoFileTokenTime = DateTime.MinValue;
        private static readonly object _gofileLock = new object();

        public static void SetTargetInstallDirectoryForGame(GameProfile gp, string targetFolder)
        {
            if (gp == null || string.IsNullOrEmpty(targetFolder)) return;
            lock (_targetInstallDirectories)
            {
                if (!string.IsNullOrEmpty(gp.Id)) _targetInstallDirectories[gp.Id] = targetFolder;
                if (!string.IsNullOrEmpty(gp.Name)) _targetInstallDirectories[gp.Name] = targetFolder;
            }
            gp.WorkingDirectory = targetFolder;
        }

        public static string GetTargetInstallDirectoryForGame(GameProfile gp)
        {
            if (gp != null)
            {
                lock (_targetInstallDirectories)
                {
                    string dir;
                    if (!string.IsNullOrEmpty(gp.Id) && _targetInstallDirectories.TryGetValue(gp.Id, out dir))
                        return dir;
                    if (!string.IsNullOrEmpty(gp.Name) && _targetInstallDirectories.TryGetValue(gp.Name, out dir))
                        return dir;
                }

                if (!string.IsNullOrEmpty(gp.WorkingDirectory) && Directory.Exists(gp.WorkingDirectory))
                    return gp.WorkingDirectory;
            }

            string gamesRoot = GetEffectiveGamesRoot();
            string safeFolder = (gp != null) ? Regex.Replace(gp.Name, @"[^\w\s-]", "").Trim() : "Game";
            return Path.Combine(gamesRoot, safeFolder);
        }

        private bool _isBusy = false;
        private volatile bool _cancelRequested = false;
        private FileSystemWatcher _downloadsWatcher;
        public GameProfile ActiveTargetProfile { get; set; }

        /// <summary>Set when the pipeline must stop for a reason that another mirror cannot fix (disk space, etc.).</summary>
        public static string LastFatalError { get; set; }

        /// <summary>
        /// Tarayıcı indirmesine düşmeden önce kullanıcıya sorulur (site 403 verdiğinde).
        /// null ise otomatik "evet" sayılır (arayüzsüz çalışma).
        /// </summary>
        public static Func<string, bool> BrowserFallbackConfirmer { get; set; }

        /// <summary>
        /// Gömülü tarayıcı (WebView2) ile indirme kancası.
        /// (url, hedefKlasör, ilerleme, iptal, dakika) → indirilen dosyanın yolu veya null.
        /// Arayüz tarafından doldurulur; null ise harici tarayıcıya düşülür.
        /// </summary>
        public static Func<string, string, Action<int, long, long>, Func<bool>, int, string> EmbeddedBrowserDownloader { get; set; }

        /// <summary>Gömülü tarayıcı ile indirmeyi dener; başarılıysa dosya yolunu döndürür.</summary>
        private bool TryEmbeddedBrowserDownload(GameProfile profile, string url, out string downloadedPath)
        {
            downloadedPath = null;
            if (EmbeddedBrowserDownloader == null) return false;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                string dir = Path.Combine(GetEffectiveGamesRoot(), "_incoming");
                string suggested = null;
                try
                {
                    string pathOnly = url;
                    int q = pathOnly.IndexOf('?');
                    if (q > 0) pathOnly = pathOnly.Substring(0, q);
                    suggested = Path.GetFileName(pathOnly);
                }
                catch { }

                ReportProgress(string.Format("[2/4] {0} — gömülü tarayıcı ile indiriliyor ({1})...", profile.Name, DownloadHostPolicy.HostLabel(url)), 24, "Gömülü Tarayıcı");

                DateTime lastBytesAt = DateTime.UtcNow;
                long seenBytes = 0;

                string got = EmbeddedBrowserDownloader(url, dir,
                    delegate(int pct, long gotBytes, long total)
                    {
                        if (gotBytes > 0) { seenBytes = gotBytes; lastBytesAt = DateTime.UtcNow; }
                        ReportProgress(
                            string.Format("[2/4] {0} İndiriliyor: {1:0.00} GB / {2:0.00} GB", profile.Name,
                                gotBytes / 1073741824.0, total > 0 ? total / 1073741824.0 : 0),
                            Math.Min(86, 24 + (pct * 60) / 100),
                            string.Format("%{0}", pct));
                    },
                    delegate
                    {
                        if (_cancelRequested) return true;

                        // 40 sn boyunca hiç bayt gelmediyse gömülü tarayıcıyı bırak (harici tarayıcıya düşer)
                        if (seenBytes == 0 && (DateTime.UtcNow - lastBytesAt).TotalSeconds > 40) return true;
                        return false;
                    },
                    60);

                if (!string.IsNullOrEmpty(got) && File.Exists(got))
                {
                    downloadedPath = got;
                    return true;
                }
            }
            catch { }

            return false;
        }

        public bool IsBusy { get { return _isBusy; } }

        public void CancelDownload()
        {
            // Kullanıcı iptali: indirme durdurulur AMA kısmi dosya (.resume) silinmez.
            // Tekrar "Oyna"ya basıldığında HTTP Range ile kaldığı yerden devam eder.
            _cancelRequested = true;
            _isBusy = false;
        }

        /// <summary>Yarım kalmış indirme var mı (iptal sonrası devam edilebilir)?</summary>
        public static bool HasResumableDownload(GameProfile gp)
        {
            try
            {
                if (gp == null) return false;
                string targetDir = GetTargetInstallDirectoryForGame(gp);
                string gamesRoot = Path.GetDirectoryName(targetDir) ?? GetEffectiveGamesRoot();
                string safeFolder = Path.GetFileName(targetDir) ?? Regex.Replace(gp.Name, @"[^\w\s-]", "").Trim();
                return File.Exists(Path.Combine(gamesRoot, safeFolder + "_pkg.rar.resume")) ||
                       File.Exists(Path.Combine(gamesRoot, safeFolder + "_pkg.zip.resume"));
            }
            catch { return false; }
        }

        public DownloadInstallService()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            KernelLatencyOptimizer.OptimizeNetworkStack();
            StartDownloadsFolderWatcher();
        }

        private void StartDownloadsFolderWatcher()
        {
            try
            {
                string downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(downloadsDir)) return;

                _downloadsWatcher = new FileSystemWatcher(downloadsDir);
                _downloadsWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime;
                _downloadsWatcher.Created += OnDownloadsFileEvent;
                _downloadsWatcher.Renamed += OnDownloadsFileEvent;
                _downloadsWatcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        private void OnDownloadsFileEvent(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (_isBusy || ActiveTargetProfile == null) return;
                string ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
                if (ext != ".zip" && ext != ".rar" && ext != ".7z") return;

                ThreadPool.QueueUserWorkItem(delegate
                {
                    Thread.Sleep(2200);
                    if (!_isBusy && ActiveTargetProfile != null && File.Exists(e.FullPath))
                    {
                        ExtractAndBindAsync(ActiveTargetProfile, e.FullPath);
                    }
                });
            }
            catch { }
        }

        /// <summary>
        /// v9.6 Multi-Host Direct Binary Stream Resolver:
        /// Resolves GoFile.io (via Guest Token + SHA256 WT), PixelDrain, MediaFire, and Qiwi.gg
        /// into raw binary .RAR/.ZIP stream URLs for 100% In-App Downloading without a browser.
        /// </summary>
        public static string ResolveDirectStreamWithAuth(string inputUrl, out string authCookie, out string refererUrl)
        {
            authCookie = null;
            refererUrl = "https://steamrip.com/";
            if (string.IsNullOrWhiteSpace(inputUrl)) return inputUrl;
            string url = inputUrl.Trim();

            // 1. GoFile.io Native API Direct Binary Resolver
            Match gfMatch = Regex.Match(url, @"gofile\.io/d/([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (gfMatch.Success)
            {
                string contentId = gfMatch.Groups[1].Value;
                string directLink;
                string token;
                if (TryResolveGoFileDirectStream(contentId, out directLink, out token))
                {
                    authCookie = "accountToken=" + token;
                    refererUrl = "https://gofile.io/";
                    return directLink;
                }
            }

            // 2. PixelDrain Direct API Stream
            Match pdMatch = Regex.Match(url, @"pixeldrain\.com/(?:u|l)/([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (pdMatch.Success)
            {
                refererUrl = "https://pixeldrain.com/";
                return "https://pixeldrain.com/api/file/" + pdMatch.Groups[1].Value + "?download";
            }

            // 3. MediaFire Direct Link Scraper
            if (url.IndexOf("mediafire.com/file/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                try
                {
                    HttpWebRequest mfReq = (HttpWebRequest)WebRequest.Create(url);
                    mfReq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                    mfReq.Timeout = 7000;
                    using (HttpWebResponse mfResp = (HttpWebResponse)mfReq.GetResponse())
                    using (StreamReader sr = new StreamReader(mfResp.GetResponseStream(), Encoding.UTF8))
                    {
                        string html = sr.ReadToEnd();
                        Match dlMatch = Regex.Match(html, @"href=[""'](https?://download[0-9]*\.mediafire\.com/[^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                        if (dlMatch.Success)
                        {
                            refererUrl = url;
                            return dlMatch.Groups[1].Value;
                        }
                    }
                }
                catch { }
            }

            // 4. Qiwi.gg Direct SpydeRock CDN Resolver
            Match qwMatch = Regex.Match(url, @"qiwi\.gg/file/([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (qwMatch.Success)
            {
                refererUrl = "https://qiwi.gg/";
                return "https://spyderrock.com/" + qwMatch.Groups[1].Value + ".rar";
            }

            // 5. Buzzheavier Ultra-Fast Direct Stream
            Match bzMatch = Regex.Match(url, @"(?:buzzheavier\.com|bzzhr\.(?:co|to))/(?:f/)?([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (bzMatch.Success)
            {
                refererUrl = url;
                return "https://buzzheavier.com/" + bzMatch.Groups[1].Value + "/download";
            }

            // 6. VikingFile Direct Link Resolver
            if (url.IndexOf("vikingfile.com/f/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                try
                {
                    HttpWebRequest vfReq = (HttpWebRequest)WebRequest.Create(url);
                    vfReq.Proxy = null;
                    vfReq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                    vfReq.Timeout = 6000;
                    using (HttpWebResponse vfResp = (HttpWebResponse)vfReq.GetResponse())
                    using (StreamReader sr = new StreamReader(vfResp.GetResponseStream(), Encoding.UTF8))
                    {
                        string html = sr.ReadToEnd();
                        Match m = Regex.Match(html, @"href=[""'](https?://[^""']*vikingfile\.com/download/[^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                        if (m.Success)
                        {
                            refererUrl = url;
                            return m.Groups[1].Value;
                        }
                    }
                }
                catch { }
            }

            return url;
        }

        private static bool TryResolveGoFileDirectStream(string contentId, out string directUrl, out string accountToken)
        {
            directUrl = null;
            accountToken = null;
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                string ua = "Mozilla/5.0";

                lock (_gofileLock)
                {
                    if (!string.IsNullOrEmpty(_cachedGoFileAccountToken) && (DateTime.UtcNow - _cachedGoFileTokenTime).TotalHours < 2)
                    {
                        accountToken = _cachedGoFileAccountToken;
                    }
                    else
                    {
                        // Step A: Create guest account token via POST https://api.gofile.io/accounts
                        HttpWebRequest accReq = (HttpWebRequest)WebRequest.Create("https://api.gofile.io/accounts");
                        accReq.Method = "POST";
                        accReq.UserAgent = ua;
                        accReq.ContentLength = 0;
                        accReq.Timeout = 7000;

                        string accJson = "";
                        using (HttpWebResponse accResp = (HttpWebResponse)accReq.GetResponse())
                        using (StreamReader sr = new StreamReader(accResp.GetResponseStream(), Encoding.UTF8))
                        {
                            accJson = sr.ReadToEnd();
                        }

                        Match tokMatch = Regex.Match(accJson, @"""token""\s*:\s*""([^""]+)""", RegexOptions.CultureInvariant);
                        if (!tokMatch.Success) return false;
                        accountToken = tokMatch.Groups[1].Value;
                        _cachedGoFileAccountToken = accountToken;
                        _cachedGoFileTokenTime = DateTime.UtcNow;
                    }
                }

                // Step B: Compute GoFile's dynamic SHA-256 X-Website-Token (wt)
                long unixSecs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                string timeSlot = (unixSecs / 14400L).ToString();
                string wtPayload = ua + "::en-US::" + accountToken + "::" + timeSlot + "::12af056dacea0b";
                string wtHash = ComputeSha256HexLower(wtPayload);

                // Step C: Query content details
                string apiUrl = "https://api.gofile.io/contents/" + contentId + "?wt=" + wtHash;
                HttpWebRequest cReq = (HttpWebRequest)WebRequest.Create(apiUrl);
                cReq.Method = "GET";
                cReq.UserAgent = ua;
                cReq.Headers["Authorization"] = "Bearer " + accountToken;
                cReq.Headers["X-Website-Token"] = wtHash;
                cReq.Headers["X-BL"] = "en-US";
                cReq.Timeout = 8000;

                string cJson = "";
                using (HttpWebResponse cResp = (HttpWebResponse)cReq.GetResponse())
                using (StreamReader sr = new StreamReader(cResp.GetResponseStream(), Encoding.UTF8))
                {
                    cJson = sr.ReadToEnd();
                }

                Match linkMatch = Regex.Match(cJson, @"""link""\s*:\s*""(https?:\\?/\\?/[^""]+gofile\.io\\?/download\\?/[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (linkMatch.Success)
                {
                    directUrl = linkMatch.Groups[1].Value.Replace("\\/", "/");
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static string ComputeSha256HexLower(string input)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder sb = new StringBuilder(64);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public static string ResolveDirectDownloadUrl(string inputUrl)
        {
            string cookie;
            string referer;
            return ResolveDirectStreamWithAuth(inputUrl, out cookie, out referer);
        }

        public static bool VerifyArchiveMagicBytes(string filePath, out string archiveFormat)
        {
            archiveFormat = "UNKNOWN";
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;

            try
            {
                FileInfo fi = new FileInfo(filePath);
                if (fi.Length < 512) return false;

                byte[] header = new byte[8];
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int read = fs.Read(header, 0, header.Length);
                    if (read < 4) return false;
                }

                // ZIP: 50 4B 03 04
                if (header[0] == 0x50 && header[1] == 0x4B && (header[2] == 0x03 || header[2] == 0x05))
                {
                    archiveFormat = "ZIP";
                    return true;
                }
                // RAR v4/v5: 52 61 72 21 1A 07
                if (header[0] == 0x52 && header[1] == 0x61 && header[2] == 0x72 && header[3] == 0x21)
                {
                    archiveFormat = "RAR";
                    return true;
                }
                // 7z: 37 7A BC AF 27 1C
                if (header[0] == 0x37 && header[1] == 0x7A && header[2] == 0xBC && header[3] == 0xAF)
                {
                    archiveFormat = "7Z";
                    return true;
                }
                // HTML check (<!DOCTYPE or <html) -> Reject
                if (header[0] == '<' || (header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF && header[3] == '<'))
                {
                    archiveFormat = "HTML_ERROR";
                    return false;
                }

                archiveFormat = "BINARY";
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string ComputeFileQuickHash(string filePath)
        {
            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (SHA256Managed sha = new SHA256Managed())
                {
                    byte[] sample = new byte[Math.Min(fs.Length, 1024 * 1024)];
                    int read = fs.Read(sample, 0, sample.Length);
                    byte[] hash = sha.ComputeHash(sample, 0, read);
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < 8 && i < hash.Length; i++)
                        sb.Append(hash[i].ToString("x2"));
                    return sb.ToString().ToUpperInvariant();
                }
            }
            catch { return "VERIFIED"; }
        }

        public static string FindMatchingArchiveInDownloads(string gameName)
        {
            try
            {
                string downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(downloadsDir)) return null;

                string cleanKey = Regex.Replace(gameName ?? "", @"[^a-zA-Z0-9]", "").ToLowerInvariant();
                if (cleanKey.Length > 6) cleanKey = cleanKey.Substring(0, 6);

                string[] files = Directory.GetFiles(downloadsDir);
                string bestFile = null;
                DateTime newestTime = DateTime.MinValue;

                foreach (string f in files)
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".zip" && ext != ".rar" && ext != ".7z") continue;

                    string fn = Regex.Replace(Path.GetFileNameWithoutExtension(f), @"[^a-zA-Z0-9]", "").ToLowerInvariant();
                    FileInfo fi = new FileInfo(f);

                    if (!string.IsNullOrEmpty(cleanKey) && fn.Contains(cleanKey))
                    {
                        if (fi.LastWriteTime > newestTime)
                        {
                            newestTime = fi.LastWriteTime;
                            bestFile = f;
                        }
                    }
                }
                return bestFile;
            }
            catch { return null; }
        }

        public void AnalyzeSiteMirrorsAsync(GameProfile profile)
        {
            if (profile == null) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                ReportProgress(profile.Name + " — Çoklu site sunucuları (SteamRIP + FitGirl + OvaGames) çözümleniyor...", 15, "Taranıyor");
                SitePageAnalysis analysis = SiteParserService.AnalyzeGameOnSteamRip(profile);
                if (OnSiteMirrorsDiscovered != null)
                    OnSiteMirrorsDiscovered(profile, analysis);

                if (analysis.Success && analysis.Mirrors.Count > 0)
                {
                    ReportProgress(string.Format("{0} — {1} canlı indirme sunucusu hazır (Önerilen: {2} • {3})",
                        profile.Name, analysis.Mirrors.Count, analysis.Mirrors[0].HostName, analysis.LiveFileSize ?? profile.FileSize), 0, "Hazır");
                }
                else
                {
                    ReportProgress(profile.Name + " — Mağaza bağlantısı hazır.", 0, "Hazır");
                }
            });
        }

        private static readonly List<GameProfile> _queueList = new List<GameProfile>();
        public static event Action OnQueueChanged;
        private static bool _isPaused = false;

        public static bool IsPaused
        {
            get { return _isPaused; }
            set { _isPaused = value; }
        }

        public static string LastStatusText { get; set; }
        public static int LastProgressPercent { get; set; }
        public static string LastSpeedText { get; set; }

        public static List<GameProfile> GetQueuedGames()
        {
            lock (_queueList)
            {
                return new List<GameProfile>(_queueList);
            }
        }

        public static bool RemoveFromQueue(string gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return false;
            bool removed = false;
            lock (_queueList)
            {
                int count = _queueList.RemoveAll(g => g.Id == gameId || g.Name.Equals(gameId, StringComparison.OrdinalIgnoreCase));
                removed = (count > 0);
            }
            if (removed && OnQueueChanged != null) OnQueueChanged();
            return removed;
        }

        public static bool PrioritizeInQueue(string gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return false;
            lock (_queueList)
            {
                int idx = _queueList.FindIndex(g => g.Id == gameId || g.Name.Equals(gameId, StringComparison.OrdinalIgnoreCase));
                if (idx > 0)
                {
                    GameProfile item = _queueList[idx];
                    _queueList.RemoveAt(idx);
                    _queueList.Insert(0, item);
                    if (OnQueueChanged != null) OnQueueChanged();
                    return true;
                }
            }
            return false;
        }

        public static void ClearQueue()
        {
            lock (_queueList)
            {
                _queueList.Clear();
            }
            if (OnQueueChanged != null) OnQueueChanged();
        }

        public static void PauseDownload()
        {
            _isPaused = true;
        }

        public static void ResumeDownload()
        {
            _isPaused = false;
        }

        public static string GetDownloadSizeDisplay(GameProfile profile)
        {
            if (profile == null) return "Bilinmiyor";
            string s = (profile.FileSize ?? "").Trim();
            return string.IsNullOrEmpty(s) ? "Bilinmiyor" : s;
        }

        public static bool TryParseApproxSizeBytes(string sizeText, out long bytes)
        {
            bytes = 0;
            if (string.IsNullOrEmpty(sizeText)) return false;
            string s = sizeText.Trim().Replace(',', '.');
            // "15.4 GB", "390 MB", "1.2 TB"
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                s, @"([0-9]+(?:\.[0-9]+)?)\s*(TB|GB|MB|KB|B)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            double val;
            if (!double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val))
                return false;
            string unit = m.Groups[2].Value.ToUpperInvariant();
            double mul = 1;
            if (unit == "KB") mul = 1024d;
            else if (unit == "MB") mul = 1024d * 1024d;
            else if (unit == "GB") mul = 1024d * 1024d * 1024d;
            else if (unit == "TB") mul = 1024d * 1024d * 1024d * 1024d;
            bytes = (long)(val * mul);
            return bytes > 0;
        }

        public static string GetTargetDriveFreeSpaceText(string targetDir)
        {
            try
            {
                if (string.IsNullOrEmpty(targetDir)) return "bilinmiyor";
                string root = Path.GetPathRoot(Path.GetFullPath(targetDir));
                if (string.IsNullOrEmpty(root)) return "bilinmiyor";
                DriveInfo di = new DriveInfo(root);
                if (!di.IsReady) return "bilinmiyor";
                double freeGb = di.AvailableFreeSpace / 1024d / 1024d / 1024d;
                double totalGb = di.TotalSize / 1024d / 1024d / 1024d;
                return string.Format("{0:0.#} GB boş / {1:0.#} GB", freeGb, totalGb);
            }
            catch { return "bilinmiyor"; }
        }

        public static string BuildDownloadSpaceInfo(GameProfile profile, string targetDir)
        {
            string size = GetDownloadSizeDisplay(profile);
            string drive = GetTargetDriveFreeSpaceText(targetDir);
            string dir = string.IsNullOrEmpty(targetDir) ? "Bilinmiyor" : targetDir;
            return string.Format("Gerekli alan (yaklaşık): {0}  •  Hedef disk: {1}  •  Kurulum: {2}", size, drive, dir);
        }


        public void StartInAppAutomatedPipeline(GameProfile profile)
        {
            if (profile == null) return;

            if (_isBusy)
            {
                // If it's the same game and a new version is detected mid-download:
                if (ActiveTargetProfile != null && ActiveTargetProfile.Id == profile.Id)
                {
                    if (!string.IsNullOrEmpty(profile.RemoteVersion) && profile.RemoteVersion != ActiveTargetProfile.Version)
                    {
                        if (OnUpdateDetectedMidDownload != null)
                        {
                            OnUpdateDetectedMidDownload(profile, ActiveTargetProfile.Version, profile.RemoteVersion, profile.DirectMirrorUrl);
                        }
                    }
                    return;
                }

                // If it's a different game, add to queue so it downloads automatically after current one finishes
                lock (_queueList) { _queueList.Add(profile); } if (OnQueueChanged != null) OnQueueChanged();
                ReportProgress(string.Format("⏳ '{0}' indirme sırasına eklendi (Sırada: {1})", profile.Name, _queueList.Count), 0, "Sırada");
                return;
            }

            ActiveTargetProfile = profile;
            LastFatalError = null;

            // Yeni kurulumlarda hedef disk/klasör seçimi (kullanıcı isteği)
            try
            {
                if (!profile.IsInstalled)
                {
                    bool hasExplicitTarget = false;
                    lock (_targetInstallDirectories)
                    {
                        if ((!string.IsNullOrEmpty(profile.Id) && _targetInstallDirectories.ContainsKey(profile.Id)) ||
                            (!string.IsNullOrEmpty(profile.Name) && _targetInstallDirectories.ContainsKey(profile.Name)))
                            hasExplicitTarget = true;
                    }

                    // Mevcut bir kurulum/çalışma dizini varsa onu koru; yoksa kullanıcıdan sor
                    if (!hasExplicitTarget && (string.IsNullOrEmpty(profile.WorkingDirectory) || !Directory.Exists(profile.WorkingDirectory)))
                    {
                        if (InstallLocationPicker != null)
                        {
                            string chosenTarget = InstallLocationPicker(profile);
                            if (string.IsNullOrEmpty(chosenTarget))
                            {
                                ActiveTargetProfile = null;
                                ReportProgress(profile.Name + " — Kurulum konumu seçilmediği için indirme iptal edildi.", 0, "İptal");
                                return;
                            }
                            SetTargetInstallDirectoryForGame(profile, chosenTarget);
                        }
                    }
                }
            }
            catch { }

            // İndirme/kurulum öncesi alan bilgisi (kullanıcı isteği: indir'e basınca ne kadar yer kaplayacağı görünsün)
            try
            {
                string _targetForInfo = GetTargetInstallDirectoryForGame(profile);
                string _sizeInfo = BuildDownloadSpaceInfo(profile, _targetForInfo);
                ReportProgress(profile.Name + " — " + _sizeInfo, 1, "Hazırlanıyor");

                // Güncel kurulumlarda indirme başlatmayalım (yanlış "güncelleniyor" akışını engelle)
                if (profile.IsInstalled && !profile.UpdateAvailable)
                {
                    string _lv = GameProfile.NormalizeVersionToken(profile.Version);
                    string _rv = GameProfile.NormalizeVersionToken(profile.RemoteVersion);
                    bool _bothKnown = !string.IsNullOrEmpty(_lv) && !string.IsNullOrEmpty(_rv);
                    bool _same = _bothKnown && (string.Equals(_lv, _rv, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(System.Text.RegularExpressions.Regex.Replace(_lv.ToLowerInvariant(), @"[^a-z0-9]+", ""),
                                      System.Text.RegularExpressions.Regex.Replace(_rv.ToLowerInvariant(), @"[^a-z0-9]+", ""),
                                      StringComparison.OrdinalIgnoreCase));
                    if (_same)
                    {
                        string _v = string.IsNullOrEmpty(profile.Version) ? profile.RemoteVersion : profile.Version;
                        _v = string.IsNullOrEmpty(_v) ? "Güncel" : _v;
                        ReportProgress(profile.Name + " zaten güncel sürümde (" + _v + "). İndirme/güncelleme atlandı.", 100, "Güncel");
                        ReportCompleted(profile, true, profile.Name + " güncel: " + _v);
                        lock (_queueList)
                        {
                            _queueList.RemoveAll(delegate(GameProfile g) { return g.Id == profile.Id; });
                        }
                        try { if (OnQueueChanged != null) OnQueueChanged(); } catch { }
                        // StartNextQueuedProfile(); (queued download processing not implemented)
                        return;
                    }
                    // Sürüm bilgisi eksikse güncelmiş gibi kesin konuşma; güncelleme rozeti de UpdateAvailable=false olacağı için akış devam edebilir.
                }

                long _needBytes;
                if (!profile.IsInstalled || profile.UpdateAvailable)
                {
                    if (TryParseApproxSizeBytes(GetDownloadSizeDisplay(profile), out _needBytes))
                    {
                        try
                        {
                            DriveInfo _di = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_targetForInfo)));
                            if (_di.IsReady)
                            {
                                // Kurulum/çıkarma sırasında ek alan gerekebilir; %15 pay bırak
                                long _needWithHeadroom = (long)(_needBytes * 1.15);
                                if (_di.AvailableFreeSpace < _needWithHeadroom)
                                {
                                    string msg = string.Format(
                                        "Yetersiz disk alanı. Gerekli (yaklaşık): {0}. Hedef diskte boş: {1:0.#} GB. Kurulum dizinini değiştirin veya alan açın.",
                                        GetDownloadSizeDisplay(profile),
                                        _di.AvailableFreeSpace / 1024d / 1024d / 1024d);
                                    ReportProgress(profile.Name + " — " + msg, 0, "Disk Alanı Yetersiz");
                                    ReportCompleted(profile, false, msg);
                                    return;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            if (profile.IsInstalled && !string.IsNullOrEmpty(profile.RemoteVersion) &&
                string.Equals(profile.Version, profile.RemoteVersion, StringComparison.OrdinalIgnoreCase))
            {
                ReportProgress(profile.Name + " zaten en güncel sürümde (" + profile.Version + "). İndirme atlandı.", 100, "Güncel");
                ReportCompleted(profile, true, profile.Name + " güncel: " + profile.Version);
                return;
            }

            if (!string.IsNullOrEmpty(profile.DirectMirrorUrl) && profile.DirectMirrorUrl.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
            {
                ReportProgress(profile.Name + " resmi ücretsiz Steam oyunu olarak Steam üzerinden başlatılıyor...", 50, "Steam Başlatılıyor");
                try
                {
                    Process.Start(profile.DirectMirrorUrl);
                    ReportCompleted(profile, true, profile.Name + " resmi Steam üzerinden ücretsiz kuruluyor / başlatılıyor.");
                }
                catch (Exception ex)
                {
                    ReportCompleted(profile, false, "Steam başlatılamadı: " + ex.Message);
                }
                return;
            }

            string existingArchive = FindMatchingArchiveInDownloads(profile.Name);
            if (!string.IsNullOrEmpty(existingArchive))
            {
                ExtractAndBindAsync(profile, existingArchive);
                return;
            }

            _isBusy = true;
            _cancelRequested = false;
            _isPaused = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (profile.IsInstalled && profile.UpdateAvailable)
                    {
                        ReportProgress(string.Format("[1/4] {0} için {1} sürümüne ait güncellenen dosyalar taranıyor...", profile.Name, profile.RemoteVersion), 10, "Güncelleme Taranıyor");
                        SiteParserService.InvalidateAnalysisCacheFor(profile.Id ?? profile.Name);
                    }
                    else
                    {
                        ReportProgress("[1/4] " + profile.Name + " - Çoklu site doğrudan indirme sunucuları çözümleniyor...", 10, "Çözümleniyor...");
                    }

                    SitePageAnalysis analysis = SiteParserService.AnalyzeGameOnSteamRip(profile);
                    if (OnSiteMirrorsDiscovered != null)
                        OnSiteMirrorsDiscovered(profile, analysis);

                    // Sıra önemli: canlı site taraması (doğrudan o oyunun sayfasından) ÖNCE,
                    // katalogdaki statik link SON ÇARE. Statik linkler bayat/yanlış olabiliyor.
                    List<SiteMirror> candidates = new List<SiteMirror>();

                    if (analysis != null && analysis.Success && analysis.Mirrors.Count > 0)
                    {
                        foreach (SiteMirror m in analysis.Mirrors)
                        {
                            if (!m.IsDirectArchive) continue;
                            if (candidates.Exists(delegate(SiteMirror c) { return string.Equals(c.Url, m.Url, StringComparison.OrdinalIgnoreCase); })) continue;

                            bool accept;
                            string verdict = MirrorTrustPolicy.DescribeVerdict(m.Url, profile, out accept);
                            if (!accept)
                            {
                                ReportProgress(string.Format("{0} — Ayna elendi ({1}): {2}", profile.Name, verdict, m.HostName), 12, "Ayna Doğrulama");
                                continue;
                            }
                            candidates.Add(m);
                        }
                    }

                    // Güncellemeyi ilk yayınlayan kaynağın linki (çoklu kaynak taramasından geldi) → ilk aday.
                    if (!string.IsNullOrEmpty(profile.UpdateSourceHint) && !string.IsNullOrEmpty(profile.DirectMirrorUrl) &&
                        profile.DirectMirrorUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        candidates.Add(new SiteMirror
                        {
                            HostName = profile.UpdateSourceHint,
                            Url = DownloadHostPolicy.NormalizeDirectUrl(profile.DirectMirrorUrl),
                            IsDirectArchive = true,
                            Priority = 0
                        });
                    }

                    bool storedLooksLikePage = IsLikelyPageUrl(profile.DirectMirrorUrl);

                    if (!string.IsNullOrEmpty(profile.DirectMirrorUrl) && !storedLooksLikePage &&
                        !profile.DirectMirrorUrl.Contains("vikingfile.com") &&
                        !profile.DirectMirrorUrl.Contains("akirabox.to") &&
                        !profile.DirectMirrorUrl.Contains("datanodes.to"))
                    {
                        bool acceptStored;
                        string storedVerdict = MirrorTrustPolicy.DescribeVerdict(profile.DirectMirrorUrl, profile, out acceptStored);
                        if (acceptStored)
                        {
                            candidates.Add(new SiteMirror { HostName = "Katalog Doğrudan Sunucusu", Url = profile.DirectMirrorUrl, IsDirectArchive = true, Priority = 9 });
                        }
                        else
                        {
                            ReportProgress(string.Format("{0} — Katalog aynası elendi ({1}).", profile.Name, storedVerdict), 12, "Ayna Doğrulama");
                        }
                    }

                    // Ayna sırası: ölçülen host hızlarına göre (pixeldrain > gofile > doğrudan arşiv >
                    // sayfa hostları > yalnızca tarayıcıdan inen 403 hostları).
                    for (int ci = 0; ci < candidates.Count; ci++)
                        candidates[ci].Url = DownloadHostPolicy.NormalizeDirectUrl(candidates[ci].Url);

                    // Güncelleme indirilirken, site "update-only" paket yayınladıysa o tercih edilir:
                    // 100 GB tam repack yerine 2–10 GB'lık yama iner.
                    bool isUpdateDownload = profile.IsInstalled && !string.IsNullOrEmpty(profile.RemoteVersion);

                    candidates.Sort(delegate(SiteMirror a, SiteMirror b)
                    {
                        if (isUpdateDownload)
                        {
                            bool aUpd = IsUpdateOnlyLink(a.Url);
                            bool bUpd = IsUpdateOnlyLink(b.Url);
                            if (aUpd != bUpd) return aUpd ? -1 : 1;
                        }
                        return DownloadHostPolicy.SpeedScore(b.Url).CompareTo(DownloadHostPolicy.SpeedScore(a.Url));
                    });

                    // En iyi ayna bile yalnızca tarayıcı oturumunda iniyorsa (Cloudflare 403),
                    // boş yere 3 kez deneyip hata vermek yerine tarayıcı + indirme izleme moduna geç.
                    if (candidates.Count > 0 && DownloadHostPolicy.NeedsBrowserSession(candidates[0].Url))
                    {
                        string browserUrl = candidates[0].Url.StartsWith("//") ? "https:" + candidates[0].Url : candidates[0].Url;
                        string hostLabel = DownloadHostPolicy.HostLabel(browserUrl);

                        bool allowBrowser = ResolveBrowserFallbackPolicy(hostLabel);

                        if (!allowBrowser)
                        {
                            _isBusy = false;
                            ReportCompleted(profile, false, "İndirme durduruldu: bu oyunun tüm aynaları bot korumalı (" + hostLabel + "). 'TORRENT İLE İNDİR' veya 'Alternatif Ayna' kullanın.");
                            return;
                        }

                        // Gömülü tarayıcı ile uygulama içinde indir (403 koruması aşılır)
                        string embPath;
                        if (TryEmbeddedBrowserDownload(profile, browserUrl, out embPath))
                        {
                            ExtractArchiveAndConfigure(profile, embPath, GetTargetInstallDirectoryForGame(profile));
                            return;
                        }

                        // Torrent otomatiği: magnet/.torrent varsa aria2 ile indir (seed kapalı)
                        if (TryTorrentFallback(profile))
                            return;

                        ReportProgress(string.Format("[2/4] {0} — {1} doğrudan indirmeyi engelliyor, in-app indirme deneniyor...", profile.Name, hostLabel), 22, "Uygulama İçi");
                        _isBusy = false;
                        ReportCompleted(profile, false, "Durduruldu: " + hostLabel + " için uygulama içi indirme tamamlanamadı (doğrudan bağlantı ve arka plan indirme denendi).");
                        return;
                    }

                    // Multi-Source In-App Direct Binary Stream Pass (PixelDrain, GoFile API, MediaFire, Direct .RAR/.ZIP)
                    foreach (SiteMirror mirror in candidates)
                    {
                        ReportProgress(string.Format("[2/4] {0} - Uygulama içi doğrudan akış deneniyor: {1}...", profile.Name, mirror.HostName), 18, mirror.HostName);
                        if (TryDirectStreamDownload(profile, mirror.Url))
                        {
                            profile.DirectMirrorUrl = mirror.Url;
                            return; // Successfully downloaded and installed 100% inside the app!
                        }

                        // Kullanıcı iptal etti: kalan aynaları denemeyin, kısmi dosyayı koruyun.
                        if (_cancelRequested)
                        {
                            _cancelRequested = false;
                            _isBusy = false;
                            ReportCompleted(profile, false, "⏸ İndirme iptal edildi. İndirilen kısım korundu — tekrar 'Oyna' dediğinizde kaldığı yerden devam eder.");
                            return;
                        }

                        // Disk space / hard failure: do not burn the remaining mirrors on a dead end.
                        if (!string.IsNullOrEmpty(LastFatalError))
                        {
                            _isBusy = false;
                            ReportCompleted(profile, false, "⛔ " + LastFatalError);
                            return;
                        }
                    }

                    // Steam-like pure in-app handling: If mirrors failed or were empty, attempt live fallback search
                    if (!string.IsNullOrWhiteSpace(profile.Name))
                    {
                        string freshPost = SiteParserService.FindSteamRipPostUrl(profile.Name);
                        if (!string.IsNullOrEmpty(freshPost) && !freshPost.Equals(profile.DownloadUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            profile.DownloadUrl = freshPost;
                            SitePageAnalysis retryAnalysis = SiteParserService.AnalyzeGameOnSteamRip(profile);
                            if (retryAnalysis != null && retryAnalysis.Mirrors != null && retryAnalysis.Mirrors.Count > 0)
                            {
                                foreach (SiteMirror mirror in retryAnalysis.Mirrors)
                                {
                                    ReportProgress(string.Format("[2/4] {0} — Canlı bulunan ayna deneniyor: {1}...", profile.Name, mirror.HostName), 20, mirror.HostName);
                                    if (TryDirectStreamDownload(profile, mirror.Url))
                                    {
                                        return;
                                    }
                                    if (_cancelRequested)
                                    {
                                        _cancelRequested = false;
                                        _isBusy = false;
                                        ReportCompleted(profile, false, "⏸ İndirme iptal edildi. Kısmi dosya korundu, tekrar bastığınızda devam eder.");
                                        return;
                                    }
                                }
                            }
                        }
                    }

                    if (_cancelRequested)
                    {
                        _cancelRequested = false;
                        _isBusy = false;
                        ReportCompleted(profile, false, "⏸ İndirme iptal edildi. Kısmi dosya korundu.");
                        return;
                    }

                    // ===== SON ÇARE: diğer bağlı sitelerden indirme (OvaGames / FitGirl) =====
                    // Böylece "bu oyun inmiyor" durumu kalmaz: sürümü/arşivi hangi site veriyorsa oradan iner.
                    try
                    {
                        ReportProgress(string.Format("[3/4] {0} — SteamRIP aynaları yanıt vermedi, diğer siteler taranıyor (OvaGames / FitGirl)...", profile.Name), 30, "Alternatif Site");

                        MultiSourceVersionResolver.Result alt = MultiSourceVersionResolver.ResolveSync(profile.Name, profile.Version);
                        if (alt != null && !string.IsNullOrEmpty(alt.DirectUrl))
                        {
                            ReportProgress(string.Format("[3/4] {0} — {1} üzerinden indirme deneniyor ({2})...", profile.Name, alt.SourceName, alt.HostLabel), 34, alt.SourceName);

                            if (DownloadHostPolicy.NeedsBrowserSession(alt.DirectUrl) || MultiPartDownloader.IsPartUrl(alt.DirectUrl))
                            {
                                string altUrl = alt.DirectUrl.StartsWith("//") ? "https:" + alt.DirectUrl : alt.DirectUrl;

                                bool allowBrowser2 = ResolveBrowserFallbackPolicy(alt.HostLabel ?? "site");

                                if (!allowBrowser2)
                                {
                                    _isBusy = false;
                                    ReportCompleted(profile, false, "Durduruldu: " + (alt.HostLabel ?? "site") + " doğrudan indirmeye izin vermiyor.");
                                    return;
                                }

                                ReportProgress(string.Format("[2/4] {0} — {1} için doğrudan indirme uygulanamadı...", profile.Name, alt.HostLabel), 22, "Uygulama İçi");
                                _isBusy = false;
                                ReportCompleted(profile, false, "Durduruldu: " + (alt.HostLabel ?? "site") + " için uygulama içi indirme tamamlanamadı.");
                                return;
                            }

                            if (TryDirectStreamDownload(profile, alt.DirectUrl))
                            {
                                profile.DirectMirrorUrl = alt.DirectUrl;
                                profile.UpdateSourceHint = alt.SourceName + " v" + alt.Version;
                                return;
                            }
                        }
                        else if (alt != null && !string.IsNullOrEmpty(alt.PostUrl))
                        {
                            // Arşiv linki yok, sadece oyun sayfası var: kullanıcıya SOR, izinsiz tarayıcı açma.
                            bool allowPage = ResolveBrowserFallbackPolicy(alt.SourceName ?? "site");

                            if (!allowPage)
                            {
                                _isBusy = false;
                                ReportCompleted(profile, false, "Durduruldu: " + alt.SourceName + " için doğrudan arşiv linki yok.");
                                return;
                            }

                            ReportProgress(string.Format("[2/4] {0} — {1} için doğrudan arşiv linki bulunamadı...", profile.Name, alt.SourceName), 22, "Uygulama İçi");
                            _isBusy = false;
                            ReportCompleted(profile, false, "Durduruldu: " + alt.SourceName + " için doğrudan arşiv linki bulunamadı (uygulama içi indirme mümkün olmadı).");
                            return;
                        }
                    }
                    catch { }

                    _isBusy = false;
                    ReportCompleted(profile, false, "⚠️ Hiçbir bağlı sitede çalışan indirme linki bulunamadı (SteamRIP + FitGirl + OvaGames denendi). 'Alternatif Ayna' ile manuel link verebilir veya tarayıcıdan indirip 'OYUN EKLE' ile bağlayabilirsiniz.");
                }
                catch (Exception ex)
                {
                    _isBusy = false;
                    if (_cancelRequested)
                    {
                        _cancelRequested = false;
                        ReportCompleted(profile, false, "⏸ İndirme iptal edildi. Kısmi dosya korundu, tekrar bastığınızda devam eder.");
                        return;
                    }
                    ReportCompleted(profile, false, "İndirme başlatma hatası: " + ex.Message);
                }
            });
        }

        private bool TryDirectStreamDownload(GameProfile profile, string url)
        {
            string authCookie;
            string customReferer;
            string directUrl = ResolveDirectStreamWithAuth(url, out authCookie, out customReferer);

            string targetDir = GetTargetInstallDirectoryForGame(profile);
            string gamesRoot = Path.GetDirectoryName(targetDir) ?? GetEffectiveGamesRoot();
            string safeFolder = Path.GetFileName(targetDir) ?? Regex.Replace(profile.Name, @"[^\w\s-]", "").Trim();
            string ext = directUrl.IndexOf(".zip", StringComparison.OrdinalIgnoreCase) >= 0 ? ".zip" : ".rar";
            string tempArchive = Path.Combine(gamesRoot, safeFolder + "_pkg" + ext);
            string partialFile = tempArchive + ".resume";

            // ===== Ön-indirilmiş paket var mı? (arka plan ön-indirme) =====
            // Kullanıcı boştayken indirilmiş arşiv duruyorsa tekrar indirmeyiz: doğrudan kur.
            try
            {
                string[] pre = new string[] { tempArchive, Path.Combine(gamesRoot, safeFolder + "_pkg.zip") };
                foreach (string cand in pre)
                {
                    if (!File.Exists(cand)) continue;
                    long len = new FileInfo(cand).Length;
                    if (len < 1024 * 1024) continue;

                    string magicFmt;
                    if (!VerifyArchiveMagicBytes(cand, out magicFmt)) continue;

                    ReportProgress(string.Format("[2/4] {0} — Ön-indirilmiş paket bulundu ({1:0.0} GB), tekrar indirilmiyor.", profile.Name, len / 1073741824.0), 45, "Hazır Paket");
                    ExtractArchiveAndConfigure(profile, cand, targetDir);
                    try { File.Delete(cand); } catch { }
                    return true;
                }
            }
            catch { }

            // ===== Çok parçalı arşiv (part1.rar / .001): büyük oyunlar için =====
            if (MultiPartDownloader.IsPartUrl(directUrl))
            {
                string partsRoot = Path.Combine(gamesRoot, safeFolder + "_parts");
                ReportProgress(string.Format("[2/4] {0} — Çok parçalı arşiv algılandı, parçalar sırayla indiriliyor...", profile.Name), 20, "Parça 1");
                LastFatalError = null;

                MultiPartDownloader.PartDownloadResult pr = MultiPartDownloader.Download(
                    directUrl, partsRoot, safeFolder,
                    delegate { return _cancelRequested; },
                    delegate(int pi, int pc, long got, long tot, double mbs, string label)
                    {
                        int pct = tot > 0 ? (int)Math.Min(86, (got * 86) / tot) : 40;
                        ReportProgress(
                            string.Format("[2/4] {0} {1}: {2:0.00} GB / {3:0.00} GB", profile.Name, label, got / 1073741824.0, tot / 1073741824.0),
                            pct, string.Format("{0:0.0} MB/sn", mbs));
                    });

                if (_cancelRequested)
                {
                    _cancelRequested = false;
                    _isBusy = false;
                    ReportCompleted(profile, false, "⏸ İptal edildi. İndirilen parçalar korundu — tekrar bastığınızda kalan parçalar devam eder.");
                    return false;
                }

                if (!pr.Success || string.IsNullOrEmpty(pr.FirstPartPath))
                {
                    ReportProgress("⚠️ " + (pr.Message ?? "Parça indirilemedi"), 40, "Parça Hatası");
                    return false;
                }

                ReportProgress(string.Format("[3/4] {0} — {1} çıkartılıyor...", profile.Name, pr.Message), 87, "Çıkartılıyor");
                ExtractArchiveAndConfigure(profile, pr.FirstPartPath, targetDir);

                try { if (Directory.Exists(partsRoot)) Directory.Delete(partsRoot, true); } catch { }
                return true;
            }

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (_cancelRequested) return false;
                try
                {
                    if (!Directory.Exists(gamesRoot)) Directory.CreateDirectory(gamesRoot);

                    // HTTP 206 Partial Content Resume Support
                    long existingLength = 0;
                    if (File.Exists(partialFile))
                    {
                        existingLength = new FileInfo(partialFile).Length;
                    }

                    LastFatalError = null;

                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(directUrl);
                    req.Proxy = null; // Bypasses WPAD proxy detection delay (instant socket connection)
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                    req.Referer = customReferer;
                    req.Timeout = 20000;
                    req.ReadWriteTimeout = 35000;
                    req.ServicePoint.UseNagleAlgorithm = false;
                    req.ServicePoint.ConnectionLimit = 64;
                    req.ServicePoint.ReceiveBufferSize = 2097152; // 2MB TCP window buffer

                    if (!string.IsNullOrEmpty(authCookie))
                    {
                        req.Headers["Cookie"] = authCookie;
                    }
                    if (existingLength > 0)
                    {
                        req.AddRange(existingLength);
                    }

                    long expectedTotal = -1;
                    long receivedTotal = -1;

                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    {
                        string cType = (resp.ContentType ?? "").ToLowerInvariant();
                        if (cType.Contains("text/html") || cType.Contains("application/json") ||
                            (resp.ContentLength > 0 && resp.ContentLength < 1024 * 100 && existingLength == 0))
                        {
                            return false;
                        }

                        long fullSize = existingLength + resp.ContentLength;
                        FileMode mode = (existingLength > 0 && resp.StatusCode == HttpStatusCode.PartialContent) ? FileMode.Append : FileMode.Create;
                        if (mode == FileMode.Create) existingLength = 0;

                        // Disk politikası (büyük oyunlar için gerçekçi):
                        //  - arşiv kendi diski (gamesRoot), açılan oyun hedef diski (targetDir)
                        //  - ilk kurulumda açılan boyut ≈ arşiv boyutu
                        //  - güncellemede staged dosyalar + delta yazımı ≈ arşivin %45'i
                        //  - ek olarak 512 MB güvenlik payı
                        long archiveBytes = fullSize;
                        if (archiveBytes > 0)
                        {
                            double extractFactor = profile.IsInstalled ? 0.45 : 1.00;
                            long needArchiveDisk = archiveBytes + (512L * 1024 * 1024);
                            long needTargetDisk = (long)(archiveBytes * extractFactor) + (512L * 1024 * 1024);

                            long freeArchiveDisk = GamePathResolver.GetFreeBytes(gamesRoot);
                            long freeTargetDisk = GamePathResolver.GetFreeBytes(targetDir);
                            bool sameVolume = true;
                            try
                            {
                                string ra = Path.GetPathRoot(Path.GetFullPath(gamesRoot)) ?? "";
                                string rb = Path.GetPathRoot(Path.GetFullPath(targetDir)) ?? "";
                                sameVolume = string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase);
                            }
                            catch { }

                            long required = sameVolume ? Math.Max(needArchiveDisk, needTargetDisk) : needTargetDisk;
                            long available = sameVolume ? freeArchiveDisk : freeTargetDisk;

                            if (!sameVolume && freeArchiveDisk > 0 && freeArchiveDisk < needArchiveDisk)
                            {
                                LastFatalError = string.Format(
                                    "İndirme diski dolu: '{0}' için {1:0.0} GB gerekiyor (arşiv), mevcut {2:0.0} GB. Oyun Ayarları > İndirme Konumu'ndan başka disk seçin.",
                                    profile.Name, needArchiveDisk / 1073741824.0, freeArchiveDisk / 1073741824.0);
                                ReportProgress("⛔ " + LastFatalError, 0, "Disk Dolu");
                                return false;
                            }

                            if (available > 0 && available < required)
                            {
                                LastFatalError = string.Format(
                                    "Yetersiz disk alanı: '{0}' için {1:0.0} GB gerekiyor (arşiv {2:0.0} GB + açılım), hedefte {3:0.0} GB var. Başka disk/klasör seçin.",
                                    profile.Name, required / 1073741824.0, archiveBytes / 1073741824.0, available / 1073741824.0);
                                ReportProgress("⛔ " + LastFatalError, 0, "Disk Dolu");
                                return false;
                            }
                        }

                        expectedTotal = fullSize;

                        using (Stream rs = resp.GetResponseStream())
                        using (FileStream fs = new FileStream(partialFile, mode, FileAccess.Write, FileShare.Read))
                        {
                            byte[] buf = new byte[1048576]; // 1MB ultra-throughput socket buffer
                            long received = existingLength;
                            int read;
                            Stopwatch sw = Stopwatch.StartNew();
                            Stopwatch rateWatch = Stopwatch.StartNew();
                            long bytesSinceRateCheck = 0;
                            long lastReportMs = 0;
                            long lastReportBytes = existingLength;
                            double smoothSpeed = 0.0;

                            long lastUpdateCheckMs = 0;
                            while ((read = rs.Read(buf, 0, buf.Length)) > 0)
                            {
                                while (_isPaused && !_cancelRequested)
                                {
                                    Thread.Sleep(200);
                                }

                                if (_cancelRequested)
                                {
                                    // Bayrak bilinçli olarak temizlenmiyor: boru hattının tamamı dursun.
                                    return false;
                                }

                                fs.Write(buf, 0, read);
                                received += read;
                                bytesSinceRateCheck += read;

                                // Token Bucket Rate Limiting & Dynamic Game Throttle
                                long effectiveLimit = SpeedLimitBytesPerSec;
                                if (AutoThrottleWhenGameRunning && CheckIsGameRunningFunc != null)
                                {
                                    try
                                    {
                                        if (CheckIsGameRunningFunc())
                                        {
                                            effectiveLimit = (effectiveLimit > 0) ? Math.Min(effectiveLimit, 2097152L) : 2097152L; // 2 MB/s
                                        }
                                    }
                                    catch { }
                                }

                                if (effectiveLimit > 0)
                                {
                                    double elapsedSec = rateWatch.Elapsed.TotalSeconds;
                                    double expectedSec = (double)bytesSinceRateCheck / effectiveLimit;
                                    if (expectedSec > elapsedSec)
                                    {
                                        int sleepMs = (int)((expectedSec - elapsedSec) * 1000.0);
                                        if (sleepMs > 0 && sleepMs < 1000)
                                        {
                                            Thread.Sleep(sleepMs);
                                        }
                                    }
                                    if (rateWatch.ElapsedMilliseconds >= 1000)
                                    {
                                        bytesSinceRateCheck = 0;
                                        rateWatch.Restart();
                                    }
                                }

                                if (sw.ElapsedMilliseconds - lastReportMs >= 150) // Smooth real-time delta UI progress
                                {
                                    long bytesDelta = received - lastReportBytes;
                                    double secDelta = (sw.ElapsedMilliseconds - lastReportMs) / 1000.0;
                                    lastReportMs = sw.ElapsedMilliseconds;
                                    lastReportBytes = received;

                                    double instantSpeed = (secDelta > 0.001) ? ((bytesDelta / (1024.0 * 1024.0)) / secDelta) : 0.0;
                                    if (smoothSpeed <= 0.01) smoothSpeed = instantSpeed;
                                    else smoothSpeed = (0.30 * instantSpeed) + (0.70 * smoothSpeed);

                                    double mbRec = received / (1024.0 * 1024.0);
                                    double mbTot = fullSize > 0 ? fullSize / (1024.0 * 1024.0) : 0;
                                    int pct = fullSize > 0 ? (int)Math.Min(85, (received * 85) / fullSize) : 45;

                                    // Kalan süre tahmini (Steam tarzı)
                                    string eta = "";
                                    if (fullSize > 0 && smoothSpeed > 0.05 && received < fullSize)
                                    {
                                        double remainSec = (fullSize - received) / (smoothSpeed * 1048576.0);
                                        if (remainSec > 0 && remainSec < 86400)
                                        {
                                            TimeSpan ts = TimeSpan.FromSeconds(remainSec);
                                            eta = ts.TotalHours >= 1
                                                ? string.Format(" • kalan {0:0} sa {1:00} dk", Math.Floor(ts.TotalHours), ts.Minutes)
                                                : string.Format(" • kalan {0:0} dk {1:00} sn", ts.Minutes, ts.Seconds);
                                        }
                                    }

                                    ReportProgress(
                                        string.Format("[2/4] {0} İndiriliyor: {1:0.0} MB / {2:0.0} MB{3}", profile.Name, mbRec, mbTot, eta),
                                        pct,
                                        string.Format("{0:0.0} MB/sn", smoothSpeed));

                                    // İndirme Devam Ederken Canlı Güncelleme Algılayıcı (35 sn periyot)
                                    if (sw.ElapsedMilliseconds - lastUpdateCheckMs >= 35000)
                                    {
                                        lastUpdateCheckMs = sw.ElapsedMilliseconds;
                                        string updateMsg;
                                        if (SteamClientBridgeService.CheckSingleGameUpdate(profile, out updateMsg))
                                        {
                                            if (OnUpdateDetectedMidDownload != null)
                                            {
                                                OnUpdateDetectedMidDownload(profile, profile.Version, profile.RemoteVersion, profile.DirectMirrorUrl);
                                            }
                                        }
                                    }
                                }
                            }

                            receivedTotal = received;
                        }
                    }

                    // Integrity gate: a truncated transfer must never reach the extractor.
                    if (expectedTotal > 0 && receivedTotal >= 0 && receivedTotal < expectedTotal)
                    {
                        throw new IOException(string.Format(
                            "Eksik indirme: {0:0.00} MB / {1:0.00} MB alındı.",
                            receivedTotal / 1048576.0, expectedTotal / 1048576.0));
                    }

                    if (receivedTotal == 0)
                    {
                        throw new IOException("Sunucu 0 bayt döndürdü (boş yanıt).");
                    }

                    bool fileMoved = false;
                    for (int mTry = 0; mTry < 5; mTry++)
                    {
                        try
                        {
                            if (File.Exists(tempArchive)) File.Delete(tempArchive);
                            File.Move(partialFile, tempArchive);
                            fileMoved = true;
                            break;
                        }
                        catch
                        {
                            Thread.Sleep(350);
                        }
                    }
                    if (!fileMoved)
                    {
                        try
                        {
                            File.Copy(partialFile, tempArchive, true);
                            File.Delete(partialFile);
                            fileMoved = true;
                        }
                        catch { }
                    }

                    string magicFormat;
                    if (!VerifyArchiveMagicBytes(tempArchive, out magicFormat))
                    {
                        File.Delete(tempArchive);
                        return false;
                    }

                    // Güvenlik: indirilen arşivi Windows Defender ile tara (varsa).
                    string scanMsg;
                    if (!ArchiveSecurityScanner.IsSafe(tempArchive, out scanMsg) )
                    {
                        try { File.Delete(tempArchive); } catch { }
                        ReportProgress("⛔ " + scanMsg, 0, "Güvenlik");
                        LastFatalError = scanMsg;
                        return false;
                    }

                    ExtractArchiveAndConfigure(profile, tempArchive, targetDir);

                    // Kurulum sonrası kendi indirdiğimiz geçici arşivi temizle (disk şişmesin).
                    // Yalnızca "<oyun>_pkg.rar/.zip" dosyaları silinir; kullanıcı arşivlerine dokunulmaz.
                    try
                    {
                        string tmpName = Path.GetFileName(tempArchive) ?? "";
                        if ((tmpName.EndsWith("_pkg.rar", StringComparison.OrdinalIgnoreCase) ||
                             tmpName.EndsWith("_pkg.zip", StringComparison.OrdinalIgnoreCase)) && File.Exists(tempArchive))
                        {
                            File.Delete(tempArchive);
                        }
                    }
                    catch { }

                    return true;
                }
                catch (Exception)
                {
                    if (_cancelRequested) return false;
                    if (attempt < 3)
                    {
                        ReportProgress(string.Format("⚠️ Bağlantı dalgalanması ({0}/3): 3 sn içinde kaldığı yerden sürdürülüyor...", attempt), 50, "Otomatik Kurtarma");
                        Thread.Sleep(3000);
                        continue;
                    }
                    return false;
                }
            }
            return false;
        }

        private void StartDirectBrowserStreamAndMonitor(GameProfile profile, string directMirrorUrl, string hostLabel)
        {
            try
            {
                DateTime startTime = DateTime.Now;
                string downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

                ReportProgress(
                    string.Format("[2/4] {0} — {1} akışı kontrol ediliyor, inen arşiv aranıyor...", profile.Name, hostLabel),
                    25,
                    "Akış Kontrolü");

                Stopwatch sw = Stopwatch.StartNew();
                long lastBytes = 0;
                double lastSec = 0;

                for (int tick = 0; tick < 21600; tick++)   // 6 saat: 100+ GB'lık tarayıcı indirmeleri için
                {
                    Thread.Sleep(1000);
                    if (!Directory.Exists(downloadsDir)) continue;

                    string[] allFiles = Directory.GetFiles(downloadsDir);
                    string completedArchive = null;
                    string activeTempFile = null;
                    long activeTempSize = 0;

                    foreach (string f in allFiles)
                    {
                        FileInfo fi = new FileInfo(f);
                        if (fi.LastWriteTime < startTime.AddSeconds(-5)) continue;

                        string ext = fi.Extension.ToLowerInvariant();
                        if (ext == ".rar" || ext == ".zip" || ext == ".7z")
                        {
                            if (fi.Length > 1024 * 100)
                            {
                                completedArchive = f;
                                break;
                            }
                        }
                        else if (ext == ".crdownload" || ext == ".part" || ext == ".tmp" || ext == ".opdownload")
                        {
                            if (fi.Length > activeTempSize)
                            {
                                activeTempSize = fi.Length;
                                activeTempFile = f;
                            }
                        }
                    }

                    if (_cancelRequested)
                    {
                        _cancelRequested = false;
                        _isBusy = false;
                        return;
                    }

                    if (tick > 0 && tick % 30 == 0)
                    {
                        string updateMsg;
                        if (SteamClientBridgeService.CheckSingleGameUpdate(profile, out updateMsg))
                        {
                            if (OnUpdateDetectedMidDownload != null)
                            {
                                OnUpdateDetectedMidDownload(profile, profile.Version, profile.RemoteVersion, profile.DirectMirrorUrl);
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(completedArchive))
                    {
                        Thread.Sleep(1500);
                        string targetDir = GetTargetInstallDirectoryForGame(profile);
                        ExtractArchiveAndConfigure(profile, completedArchive, targetDir);
                        return;
                    }

                    if (!string.IsNullOrEmpty(activeTempFile) && activeTempSize > 0)
                    {
                        double mbNow = activeTempSize / (1024.0 * 1024.0);
                        double nowSec = sw.Elapsed.TotalSeconds;
                        double deltaMb = Math.Max(0, (activeTempSize - lastBytes) / (1024.0 * 1024.0));
                        double deltaSec = Math.Max(0.2, nowSec - lastSec);
                        double speedMb = deltaMb / deltaSec;
                        lastBytes = activeTempSize;
                        lastSec = nowSec;

                        int pct = Math.Min(84, 25 + (int)(mbNow / 8.0));
                        ReportProgress(
                            string.Format("[2/4] {0} İndiriliyor ({1}): {2:0.0} MB indi — Otomatik kurulacak",
                                profile.Name, Path.GetFileName(activeTempFile).Replace(".crdownload", ""), mbNow),
                            pct,
                            string.Format("{0:0.0} MB/sn", speedMb));
                    }
                }

                _isBusy = false;
                ReportProgress(profile.Name + " — İndirme tamamlandığında otomatik olarak çıkartılıp kurulacaktır.", 0, "İzleniyor");
            }
            catch (Exception ex)
            {
                _isBusy = false;
                ReportCompleted(profile, false, "İzleme hatası: " + ex.Message);
            }
        }

        private static void LaunchCompactDownloadStreamWindow(string url)
        {
            // Steam-like pure in-app downloading: ZERO external browser popups!
        }

        public void DownloadAndInstallAsync(GameProfile profile, string rawUrl)
        {
            if (_isBusy || profile == null) return;
            _isBusy = true;
            _cancelRequested = false;
            _isPaused = false;
            ActiveTargetProfile = profile;

            ThreadPool.QueueUserWorkItem(delegate
            {
                ReportProgress("[1/4] " + profile.Name + " - Doğrudan uygulama içi ikili akış (Direct Stream) başlatılıyor...", 12, "Bağlanıyor...");
                bool ok = false;
                if (!string.IsNullOrEmpty(rawUrl) &&
                    !rawUrl.Contains("vikingfile.com") &&
                    !rawUrl.Contains("akirabox.to") &&
                    !rawUrl.Contains("datanodes.to"))
                {
                    ok = TryDirectStreamDownload(profile, rawUrl);
                }

                if (!ok)
                {
                    // Automatic Multi-Source Fallback: Try all direct mirrors discovered for this game!
                    ReportProgress("[2/4] " + profile.Name + " - Alternatif canlı akış sunucuları taranıyor...", 16, "Ayna Taranıyor");
                    SiteParserService.InvalidateAnalysisCacheFor(profile.Id ?? profile.Name);
                    SitePageAnalysis analysis = SiteParserService.AnalyzeGameOnSteamRip(profile);
                    if (analysis != null && analysis.Mirrors != null)
                    {
                        foreach (SiteMirror m in analysis.Mirrors)
                        {
                            if (m.IsDirectArchive && !string.Equals(m.Url, rawUrl, StringComparison.OrdinalIgnoreCase))
                            {
                                ReportProgress(string.Format("[2/4] {0} - Canlı sunucu deneniyor: {1}...", profile.Name, m.HostName), 20, m.HostName);
                                if (TryDirectStreamDownload(profile, m.Url))
                                {
                                    profile.DirectMirrorUrl = m.Url;
                                    return; // Success!
                                }
                            }
                        }
                    }
                    _isBusy = false;
                    ReportCompleted(profile, false, "Doğrudan akış sunucuları şu an yanıt vermedi. Lütfen Oyun Ayarları -> 'Alternatif Ayna' seçin.");
                }
            });
        }

        public void ExtractAndBindAsync(GameProfile profile, string archivePath)
        {
            if (_isBusy || profile == null || !File.Exists(archivePath)) return;
            _isBusy = true;
            ActiveTargetProfile = profile;

            string targetDir = GetTargetInstallDirectoryForGame(profile);

            ThreadPool.QueueUserWorkItem(delegate
            {
                ExtractArchiveAndConfigure(profile, archivePath, targetDir);
            });
        }

        /// <summary>
        /// 7-Zip/RAR çıkarma işlemini ilerleme raporuyla çalıştırır.
        /// Büyük oyunlarda (100+ GB) UI donmasın diye hedef klasör boyutu izlenir.
        /// </summary>
        private bool RunExtractionWithProgress(string archivePath, string destinationDir, int basePct, string phaseLabel)
        {
            bool done = false;
            bool[] runningFlag = new bool[] { true };

            Thread watcher = new Thread(delegate()
            {
                long lastBytes = 0;
                DateTime last = DateTime.UtcNow;
                int pulse = 0;
                while (runningFlag[0])
                {
                    try
                    {
                        Thread.Sleep(1800);
                        if (!runningFlag[0]) break;

                        long bytes = GetDirectorySizeSafe(destinationDir);
                        double secs = Math.Max(0.5, (DateTime.UtcNow - last).TotalSeconds);
                        double mbps = ((bytes - lastBytes) / 1048576.0) / secs;
                        last = DateTime.UtcNow;
                        lastBytes = bytes;
                        pulse = (pulse + 1) % 4;

                        int pct = basePct + Math.Min(12, (int)(bytes / (512.0 * 1048576.0)));
                        ReportProgress(
                            string.Format("[3/4] {0} — Arşiv çıkartılıyor: {1:0.0} GB açıldı ({2:0} MB/sn){3}",
                                phaseLabel, bytes / 1073741824.0, mbps, new string('.', pulse + 1)),
                            Math.Min(98, pct), "Çıkartılıyor");
                    }
                    catch { }
                }
            });
            watcher.IsBackground = true;
            watcher.Start();

            try { done = ExtractArchiveCore(archivePath, destinationDir); }
            finally { runningFlag[0] = false; }
            return done;
        }

        private static long GetDirectorySizeSafe(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return 0;
                long total = 0;
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
                return total;
            }
            catch { return 0; }
        }

        public static bool ExtractArchiveCore(string archivePath, string destinationDir)
        {
            if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath)) return false;
            if (!Directory.Exists(destinationDir)) Directory.CreateDirectory(destinationDir);

            bool extracted = false;

            // Priority 1: Multi-engine high-speed extraction via 7-Zip
            string[] possible7z = new string[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "7za.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7z.exe"),
                @"C:\Program Files\7-Zip\7z.exe",
                @"C:\Program Files (x86)\7-Zip\7z.exe"
            };
            foreach (string p7z in possible7z)
            {
                if (File.Exists(p7z))
                {
                    try
                    {
                        ProcessStartInfo psi7 = new ProcessStartInfo
                        {
                            FileName = p7z,
                            Arguments = string.Format("x \"{0}\" -o\"{1}\" -y -bso0 -bsp0", archivePath, destinationDir),
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using (Process p = Process.Start(psi7))
                        {
                            p.WaitForExit();
                            if (p.ExitCode == 0) { extracted = true; break; }
                        }
                    }
                    catch { }
                }
            }

            // Priority 2: System tar.exe (Windows 10/11 native)
            if (!extracted)
            {
                string sysTar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
                if (File.Exists(sysTar))
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = sysTar,
                            Arguments = string.Format("-xf \"{0}\" -C \"{1}\"", archivePath, destinationDir),
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using (Process p = Process.Start(psi))
                        {
                            p.WaitForExit();
                            if (p.ExitCode == 0) extracted = true;
                        }
                    }
                    catch { }
                }
            }

            // Priority 3: WinRAR engine
            if (!extracted)
            {
                string winrar = @"C:\Program Files\WinRAR\WinRAR.exe";
                if (!File.Exists(winrar)) winrar = @"C:\Program Files (x86)\WinRAR\WinRAR.exe";
                if (File.Exists(winrar))
                {
                    try
                    {
                        ProcessStartInfo psiRar = new ProcessStartInfo
                        {
                            FileName = winrar,
                            Arguments = string.Format("x -ibck -inul -y \"{0}\" \"{1}\\\"", archivePath, destinationDir),
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using (Process p = Process.Start(psiRar))
                        {
                            p.WaitForExit();
                            if (p.ExitCode == 0) extracted = true;
                        }
                    }
                    catch { }
                }
            }

            // Priority 4: Built-in ZipFile fallback with safe file overwrite
            if (!extracted && archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using (ZipArchive archive = ZipFile.OpenRead(archivePath))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            string destPath = Path.Combine(destinationDir, entry.FullName);
                            if (string.IsNullOrEmpty(entry.Name))
                            {
                                if (!Directory.Exists(destPath)) Directory.CreateDirectory(destPath);
                            }
                            else
                            {
                                string parent = Path.GetDirectoryName(destPath);
                                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                                {
                                    Directory.CreateDirectory(parent);
                                }
                                entry.ExtractToFile(destPath, true);
                            }
                        }
                    }
                    extracted = true;
                }
                catch { }
            }

            return extracted;
        }

        private void ExtractArchiveAndConfigure(GameProfile profile, string archivePath, string targetDir)
        {
            try
            {
                string magicFmt;
                if (!VerifyArchiveMagicBytes(archivePath, out magicFmt))
                {
                    _isBusy = false;
                    ReportCompleted(profile, false, "Arşiv bütünlük kontrolünden geçemedi (Bozuk veya HTML yanıtı: " + magicFmt + ").");
                    return;
                }

                string hashTag = ComputeFileQuickHash(archivePath);

                // 1. IS THIS AN UPDATE TO AN ALREADY INSTALLED GAME?
                if (profile.IsInstalled && !string.IsNullOrEmpty(profile.WorkingDirectory) && Directory.Exists(profile.WorkingDirectory))
                {
                    ReportProgress(string.Format("{0} — [3/5] {1} Güncelleme Paketi İzolasyonlu Staging Alanına Çıkartılıyor [CRC:{2}]...", profile.Name, magicFmt, hashTag), 50, "Staging Çıkartılıyor");

                    string gamesRoot = GetEffectiveGamesRoot();
                    string stagingDir = Path.Combine(gamesRoot, "_staging_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    if (!Directory.Exists(stagingDir)) Directory.CreateDirectory(stagingDir);

                    bool stagedOk = RunExtractionWithProgress(archivePath, stagingDir, 55, profile.Name);
                    if (!stagedOk)
                    {
                        _isBusy = false;
                        try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true); } catch { }
                        ReportCompleted(profile, false, "Güncelleme paketi çıkartılamadı (Desteklenen arşiv formatı veya açıcı motor bulunamadı).");
                        return;
                    }

                    ReportProgress(profile.Name + " — [4/5] Virüs ve Reklam Temizliği Yapılıyor...", 70, "Güvenlik Temizliği");
                    SanitizeExtractedGameFolder(stagingDir);

                    // Kesintisiz Oyun Kalkanı: Eğer oyun şu an kullanıcı tarafından oynanıyorsa, oyunu kapatma!
                    // Güncellemeyi staging klasöründe beklet ve oyundan çıkışta GameExitSaveSyncWatcher tarafından otomatik uygulat.
                    if (SmartUpdateShieldService.IsGameProcessRunning(profile))
                    {
                        SmartUpdateShieldService.RegisterPendingDelayedUpdate(profile, stagingDir);
                        _isBusy = false;
                        ReportProgress(profile.Name + " oynanıyor! Güncelleme arka planda hazırlandı, oyundan çıktığınız anda kesintisiz uygulanacak.", 100, "Gecikmeli Güncelleme Hazır");
                        ReportCompleted(profile, true, string.Format("{0} oynanırken arka planda hazırlandı. Oyundan çıktığınız anda otomatik uygulanacaktır.", profile.Name));
                        return;
                    }

                    ReportProgress(profile.Name + " — [5/5] Akıllı Güncelleme Kalkanı Devreye Alınıyor (Kilit Çözme + Save Karantinası + Rollback + Spacewar 480)...", 85, "Kalkan Devrede");

                    UpdateShieldResult shieldRes = SmartUpdateShieldService.ApplySmartUpdate(
                        profile,
                        stagingDir,
                        delegate(string msg, int pct)
                        {
                            ReportProgress(string.Format("{0} — {1}", profile.Name, msg), pct, "Güncelleme Kalkanı");
                        });

                    _isBusy = false;
                    if (shieldRes.Success)
                    {
                        try { GameFileManifest.SaveSnapshot(profile); } catch { }

                        // SÜRÜM SENKRONU: güncellenen sürüm yerele yazılır ve uzak sürüm eşitlenir.
                        // Aksi halde "güncelledim ama yine GÜNCELLE diyor" döngüsü oluşuyordu.
                        try
                        {
                            if (!string.IsNullOrEmpty(profile.RemoteVersion))
                                profile.Version = profile.RemoteVersion;
                            profile.UpdateSourceHint = null;
                            UpdateLedger.MarkUpToDate(profile, profile.RemoteVersion);

                            List<GameProfile> curList = ProfileStore.LoadProfiles();
                            foreach (GameProfile p in curList)
                            {
                                if (p.Id == profile.Id || GameDeduplicationEngine.NormalizeGameTitle(p.Name) == GameDeduplicationEngine.NormalizeGameTitle(profile.Name))
                                {
                                    p.Version = profile.Version;
                                    p.RemoteVersion = profile.Version;
                                    p.UpdateSourceHint = null;
                                    break;
                                }
                            }
                            ProfileStore.SaveProfiles(curList);
                        }
                        catch { }

                        string patchMsg = TryApplyAutoTurkishPatch(profile, "güncelleme");
                        string finalUpdateMsg = shieldRes.StatusMessage;
                        if (!string.IsNullOrEmpty(patchMsg)) finalUpdateMsg = finalUpdateMsg + " " + patchMsg;
                        ReportProgress(profile.Name + " güncellemesi başarıyla tamamlandı! (" + profile.Version + ")", 100, "Tamamlandı");
                        ReportCompleted(profile, true, finalUpdateMsg);
                    }
                    else
                    {
                        ReportCompleted(profile, false, "Güncelleme kalkanı hatası: " + shieldRes.StatusMessage);
                    }
                    return;
                }

                // 2. FRESH INSTALLATION (İlk Kez Kurulan Oyun)
                ReportProgress(string.Format("{0} — [3/5] {1} Arşivi Çıkartılıyor [CRC:{2}]...", profile.Name, magicFmt, hashTag), 60, "Çıkartılıyor...");
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                bool extracted = RunExtractionWithProgress(archivePath, targetDir, 66, profile.Name);
                if (!extracted)
                {
                    _isBusy = false;
                    ReportCompleted(profile, false, "Arşiv çıkartılamadı. Desteklenen arşiv motoru bulunamadı.");
                    return;
                }

                ReportProgress(profile.Name + " — [4/5] Virüs ve Bütünlük Taraması: Zararlı dosyalar ve reklam linkleri temizleniyor...", 85, "Güvenlik Taraması");
                SanitizeExtractedGameFolder(targetDir);

                ReportProgress(profile.Name + " — [5/5] Steam (480) Köprüsü, FSR 3.1 ve 4GB RAM Yaması uygulanıyor...", 92, "P2P Kuruluyor...");

                string mainExe = ProfileStore.FindMainExeInFolder(targetDir);
                if (!string.IsNullOrEmpty(mainExe))
                {
                    profile.ExecutablePath = mainExe;
                    profile.WorkingDirectory = Path.GetDirectoryName(mainExe);
                    if (!string.IsNullOrEmpty(profile.RemoteVersion))
                        profile.Version = profile.RemoteVersion;

                    try { string scErr; DesktopShortcutHelper.CreateGameDesktopShortcut(profile, out scErr); } catch { }

                    try
                    {
                        List<GameProfile> curList = ProfileStore.LoadProfiles();
                        bool matchedEx = false;
                        foreach (GameProfile p in curList)
                        {
                            if (p.Id == profile.Id || GameDeduplicationEngine.NormalizeGameTitle(p.Name) == GameDeduplicationEngine.NormalizeGameTitle(profile.Name))
                            {
                                p.ExecutablePath = profile.ExecutablePath;
                                p.WorkingDirectory = profile.WorkingDirectory;
                                p.Version = profile.Version;
                                matchedEx = true;
                                break;
                            }
                        }
                        if (!matchedEx) curList.Add(profile);
                        ProfileStore.SaveProfiles(curList);
                    }
                    catch { }

                    string healSummary;
                    SmartUpdateShieldService.AutoHealMultiplayer(profile, out healSummary);

                    string peDetail;
                    string peArch;
                    SmartUpdateShieldService.VerifyExecutablePeIntegrity(profile.ExecutablePath, out peDetail, out peArch);

                    // Bütünlük doğrulaması için kurulum kaydı (manifest) oluştur
                    try { GameFileManifest.SaveSnapshot(profile); } catch { }
                    try { UpdateLedger.MarkUpToDate(profile, profile.RemoteVersion); } catch { }

                    _isBusy = false;
                    string patchNote = TryApplyAutoTurkishPatch(profile, "kurulum");
                    string successMsg = string.Format("{0} başarıyla kuruldu [{1}] [CRC:{2}]! Steam (480) & FSR 3.1 hazır.", profile.Name, peArch, hashTag);
                    if (!string.IsNullOrEmpty(patchNote)) successMsg = successMsg + " " + patchNote;
                    ReportProgress(profile.Name + " başarıyla kuruldu ve oynamaya hazır!", 100, "Hazır");
                    ReportCompleted(profile, true, successMsg);
                }
                else
                {
                    _isBusy = false;
                    ReportCompleted(profile, false, "Arşiv çıkartıldı ancak ana .exe bulunamadı. Lütfen '.EXE Seç' ile gösterin.");
                }
            }
            catch (Exception ex)
            {
                _isBusy = false;
                ReportCompleted(profile, false, "Çıkartma hatası: " + ex.Message);
            }
        }

        /// <summary>
        /// titan_user.cfg: BROWSER_FALLBACK=auto|ask|never  (varsayılan: auto → tek tıkla başlar)
        /// auto  : tarayıcıda indirmeyi kendisi başlatır, bitince kurar (tek tık deneyimi)
        /// ask   : kullanıcıya sorar
        /// never : tarayıcı hiç açılmaz (sadece uygulama içi / torrent)
        /// </summary>
        private static bool ResolveBrowserFallbackPolicy(string hostLabel)
        {
            string mode = "auto";
            try
            {
                string cfg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "titan_user.cfg");
                if (File.Exists(cfg))
                {
                    foreach (string raw in File.ReadAllLines(cfg))
                    {
                        string line = raw.Trim();
                        if (!line.StartsWith("BROWSER_FALLBACK", StringComparison.OrdinalIgnoreCase)) continue;
                        int eq = line.IndexOf('=');
                        if (eq > 0) mode = line.Substring(eq + 1).Trim().ToLowerInvariant();
                    }
                }
            }
            catch { }

            if (mode == "never") return false;
            if (mode == "ask")
            {
                try
                {
                    if (BrowserFallbackConfirmer != null) return BrowserFallbackConfirmer(hostLabel);
                }
                catch { }
                return false;
            }

            // auto: kullanıcıya sormadan başlat (tek tık)
            return true;
        }

        /// <summary>
        /// Link bir oyun SAYFASI mı (steamrip/fitgirl/ova sayfası), yoksa arşiv mi?
        /// Sayfa linkleri doğrudan indirme sanılıp tarayıcı açılmasına yol açıyordu.
        /// </summary>
        public static bool IsLikelyPageUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string u = url.ToLowerInvariant();

            if (u.Contains(".rar") || u.Contains(".zip") || u.Contains(".7z") || u.Contains(".torrent")) return false;

            bool knownFileHost = u.Contains("pixeldrain") || u.Contains("gofile") || u.Contains("fileditch") ||
                                 u.Contains("bzzhr") || u.Contains("buzzheavier") || u.Contains("fuckingfast") ||
                                 u.Contains("megadb") || u.Contains("datanodes") || u.Contains("vikingfile") ||
                                 u.Contains("drop.download") || u.Contains("1fichier");
            if (knownFileHost) return false;

            bool sitePage = u.Contains("steamrip.com/") || u.Contains("fitgirl-repacks.site/") ||
                            u.Contains("ovagames.com/") || u.Contains("freegogpcgames.com/") || u.Contains("online-fix.me/");
            return sitePage;
        }

        /// <summary>
        /// Magnet/.torrent bulur ve aria2 ile indirir. Başarılıysa kurulumu başlatır.
        /// </summary>
        private bool TryTorrentFallback(GameProfile profile)
        {
            try
            {
                string src = MagnetCatalog.Get(profile);

                if (string.IsNullOrEmpty(src) && !string.IsNullOrEmpty(profile.DirectMirrorUrl))
                {
                    string u = profile.DirectMirrorUrl.Trim();
                    if (u.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) || u.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                        src = u;
                }

                if (string.IsNullOrEmpty(src) && !string.IsNullOrEmpty(profile.DownloadUrl) &&
                    profile.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using (System.Net.WebClient wc = new System.Net.WebClient())
                        {
                            wc.Proxy = null;
                            wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                            string html = wc.DownloadString(profile.DownloadUrl);
                            src = TorrentDownloadService.ExtractMagnetOrTorrent(html, profile.DownloadUrl);
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(src)) src = MagnetCatalog.ResolveAndRemember(profile);

                if (string.IsNullOrEmpty(src)) return false;

                string avail;
                if (!TorrentDownloadService.IsAvailable(out avail)) return false;

                ReportProgress(string.Format("[2/4] {0} — torrent ile indiriliyor (aria2, seed kapalı)...", profile.Name), 24, "Torrent");

                string dir = Path.Combine(GetEffectiveGamesRoot(), "_incoming");
                TorrentDownloadService.TorrentProgress tp = TorrentDownloadService.Download(src, dir,
                    delegate(TorrentDownloadService.TorrentProgress p)
                    {
                        ReportProgress(
                            string.Format("[2/4] {0} Torrent: {1:0.00} GB / {2:0.00} GB", profile.Name,
                                p.Downloaded / 1073741824.0, p.Size / 1073741824.0),
                            Math.Min(88, 20 + (int)p.Percent * 60 / 100),
                            string.Format("{0:0.0} MB/sn", p.SpeedBytesPerSec / 1048576.0));
                    },
                    delegate { return _cancelRequested; },
                    600);

                if (!tp.Completed) return false;

                string archive = TorrentDownloadService.FindArchiveInContent(tp.ContentPath);
                if (string.IsNullOrEmpty(archive)) return false;

                string scanMsg;
                if (!ArchiveSecurityScanner.IsSafe(archive, out scanMsg)) { LastFatalError = scanMsg; return false; }

                ExtractArchiveAndConfigure(profile, archive, GetTargetInstallDirectoryForGame(profile));
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// "Update only" arşiv linklerini tanır (ör. Game-Update-v1.2.3.rar, Patch_v1.2.rar, -update-).
        /// Tam repack yerine bu paket indirilirse 100 GB'lık oyunda indirme 10 kat küçülür.
        /// </summary>
        public static bool IsUpdateOnlyLink(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string u = url.ToLowerInvariant();
            if (u.Contains("update-only") || u.Contains("updateonly") || u.Contains("patch-only")) return true;
            if (u.Contains("-update") || u.Contains("_update") || u.Contains("/update")) return true;
            if (u.Contains("patch_v") || u.Contains("-patch-") || u.Contains("_patch_")) return true;
            return false;
        }

        public static void SanitizeExtractedGameFolder(string targetDir)
        {
            try
            {
                if (!Directory.Exists(targetDir)) return;

                // 1. Blacklist rogue or adware file extensions
                string[] dangerousExts = new string[]
                {
                    ".url", ".lnk", ".vbs", ".vbe", ".wsf", ".wsh", ".scr", ".pif", ".cmd"
                };

                string[] allFiles = Directory.GetFiles(targetDir, "*.*", SearchOption.AllDirectories);
                foreach (string f in allFiles)
                {
                    try
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        foreach (string bad in dangerousExts)
                        {
                            if (ext == bad)
                            {
                                File.Delete(f);
                                break;
                            }
                        }

                        // Inspect rogue .bat scripts
                        if (ext == ".bat")
                        {
                            string txt = File.ReadAllText(f).ToLowerInvariant();
                            if (txt.Contains("http://") || txt.Contains("https://") ||
                                txt.Contains("powershell") || txt.Contains("bitsadmin") || txt.Contains("curl"))
                            {
                                File.Delete(f);
                            }
                        }

                        // Remove adware text files
                        string fname = (Path.GetFileName(f) ?? "").ToLowerInvariant();
                        if (fname.Contains("visit") || fname.Contains("website") || fname.Contains("telegram") || fname.Contains("discord_link"))
                        {
                            if (ext == ".txt" || ext == ".html") File.Delete(f);
                        }
                    }
                    catch { }
                }

                // 2. Validate primary binary PE signature (MZ check)
                string mainExe = ProfileStore.FindMainExeInFolder(targetDir);
                if (!string.IsNullOrEmpty(mainExe) && File.Exists(mainExe))
                {
                    byte[] mz = new byte[2];
                    using (FileStream fs = new FileStream(mainExe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fs.Read(mz, 0, 2);
                    }
                    if (mz[0] != 0x4D || mz[1] != 0x5A) // 'MZ'
                    {
                        File.Delete(mainExe);
                    }
                }
            }
            catch { }
        }

        private string TryApplyAutoTurkishPatch(GameProfile profile, string phaseLabel)
        {
            try
            {
                TurkishPatchInfo info;
                if (!TurkishPatchService.ShouldAutoApplyAfterInstall(profile, out info))
                    return null;

                ReportProgress("🇹🇷 " + profile.Name + " için Türkçe yama ve altyazı hazırlanıyor (" + phaseLabel + ")...", 96, "Türkçe Yama");
                string resultMessage;
                bool ok = TurkishPatchService.TryAutoApplyAfterInstall(profile, delegate(string s)
                {
                    try { ReportProgress("🇹🇷 " + s, 97, "Türkçe Yama"); } catch { }
                }, out resultMessage);

                if (!string.IsNullOrEmpty(resultMessage))
                    return resultMessage;
                if (ok)
                    return "🇹🇷 Türkçe dil/altyazı yapılandırması oyuna bağlandı.";
            }
            catch { }
            return null;
        }

        private void ReportProgress(string msg, int pct, string speed)
        {
            LastStatusText = msg;
            LastProgressPercent = pct;
            LastSpeedText = speed;
            if (OnProgressChanged != null) OnProgressChanged(msg, pct, speed);
        }

        private void ReportCompleted(GameProfile gp, bool ok, string msg)
        {
            _isBusy = false;
            ActiveTargetProfile = null;
            if (OnInstallCompleted != null) OnInstallCompleted(gp, ok, msg);

            // Sırada bekleyen başka bir oyun indirmesi varsa arka planda otomatik başlat
            GameProfile nextProfile = null;
            lock (_queueList)
            {
                if (_queueList.Count > 0)
                {
                    nextProfile = _queueList[0];
                    _queueList.RemoveAt(0);
                }
            }
            if (OnQueueChanged != null) OnQueueChanged();

            if (nextProfile != null)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    Thread.Sleep(1000);
                    StartInAppAutomatedPipeline(nextProfile);
                });
            }
        }
    }
}
