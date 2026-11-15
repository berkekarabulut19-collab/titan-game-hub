using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TitanGameHub
{
    public static class ImageCacheService
    {
        private static readonly string CacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache");
        private static readonly Dictionary<string, BitmapImage> MemoryCache = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> CacheLruList = new List<string>();
        private const int MaxMemoryCacheItems = 120;
        private static readonly HashSet<string> InFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim DownloadGate = new SemaphoreSlim(24, 24);

        private static readonly Dictionary<string, string> SteamSearchCoverCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string ResolveSteamHeaderFallback(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName)) return null;
            string clean = Regex.Replace(gameName, @"\[[^\]]*\]|\([^\)]*\)", " ");
            clean = Regex.Replace(clean, @"[–—-]\s*(?:v\d|Build|CL#|HotFix|MULTi|ElAmigos|CODEX|PLAZA|SKIDROW|RUNE|FLT|TENOKE|DODI|FitGirl).*$", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\s+", " ").Trim();
            if (clean.Length < 2) return null;

            lock (SteamSearchCoverCache)
            {
                string cached;
                if (SteamSearchCoverCache.TryGetValue(clean, out cached))
                    return cached;
            }

            string foundUrl = null;
            try
            {
                using (TimeoutWebClient swc = new TimeoutWebClient(4500))
                {
                    swc.Proxy = null;
                    swc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    string api = "https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(clean) + "&l=english&cc=US";
                    string json = swc.DownloadString(api);
                    Match m = Regex.Match(json, @"""id""\s*:\s*(\d{2,8})\b");
                    if (m.Success && m.Groups[1].Value != "480")
                        foundUrl = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/" + m.Groups[1].Value + "/header.jpg";
                }
            }
            catch { }

            lock (SteamSearchCoverCache)
            {
                SteamSearchCoverCache[clean] = foundUrl;
            }
            return foundUrl;
        }

        private sealed class TimeoutWebClient : WebClient
        {
            private readonly int _timeoutMs;
            public TimeoutWebClient(int timeoutMs) { _timeoutMs = timeoutMs; }
            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest req = base.GetWebRequest(address);
                if (req != null)
                {
                    req.Timeout = _timeoutMs;
                    HttpWebRequest hreq = req as HttpWebRequest;
                    if (hreq != null) hreq.ReadWriteTimeout = _timeoutMs;
                }
                return req;
            }
        }

        static ImageCacheService()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288 | SecurityProtocolType.Tls12;
                ServicePointManager.DefaultConnectionLimit = 64;
                ServicePointManager.Expect100Continue = false;
            }
            catch { }
            try
            {
                if (!Directory.Exists(CacheDir))
                    Directory.CreateDirectory(CacheDir);

                // Background cache maintenance: purge corrupt <1KB files or entries older than 30 days
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        DateTime cutoff = DateTime.Now.AddDays(-30);
                        string[] files = Directory.GetFiles(CacheDir, "*.jpg");
                        foreach (string f in files)
                        {
                            try
                            {
                                FileInfo fi = new FileInfo(f);
                                if (fi.Length < 1024 || fi.LastAccessTime < cutoff)
                                    fi.Delete();
                            }
                            catch { }
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }

        private static void AddToMemoryCache(string key, BitmapImage bmp)
        {
            if (string.IsNullOrEmpty(key) || bmp == null) return;
            lock (MemoryCache)
            {
                if (!MemoryCache.ContainsKey(key))
                {
                    if (CacheLruList.Count >= MaxMemoryCacheItems)
                    {
                        string oldest = CacheLruList[0];
                        CacheLruList.RemoveAt(0);
                        MemoryCache.Remove(oldest);
                    }
                    CacheLruList.Add(key);
                }
                MemoryCache[key] = bmp;
            }
        }

        public static void BindImageAsync(Border targetBorder, GameProfile gp, int decodeWidth, double opacity = 1.0)
        {
            BindImageAsync(targetBorder, gp, decodeWidth, opacity, null);
        }

        private static bool IsUsableImageUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string u = url.Trim();
            if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return false;
            string pathOnly = u.Split('?')[0];
            if (pathOnly.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        /// <summary>
        /// onDone(true/false): görsel yüklendi mi? Disk okuma ve decode tamamen arka planda yapılır,
        /// UI thread asla kilitlenmez.
        /// </summary>
        public static void BindImageAsync(Border targetBorder, GameProfile gp, int decodeWidth, double opacity, Action<bool> onDone)
        {
            if (targetBorder == null || gp == null)
            {
                if (onDone != null) onDone(false);
                return;
            }

            string bUrl = IsUsableImageUrl(gp.BannerUrl) ? gp.BannerUrl.Trim() : null;
            string cUrl = IsUsableImageUrl(gp.CapsuleUrl) ? gp.CapsuleUrl.Trim() : null;
            string primaryUrl = bUrl ?? cUrl;
            string fallbackUrl = cUrl ?? bUrl;
            string steamAppId = (gp.SteamAppId ?? "").Trim();
            bool hasSteamId = !string.IsNullOrEmpty(steamAppId) && steamAppId != "480" && Regex.IsMatch(steamAppId, @"^\d{2,8}$");
            bool isWeakOrHotlinkUrl = !string.IsNullOrEmpty(primaryUrl) &&
                (primaryUrl.IndexOf("crotorrents.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 primaryUrl.IndexOf("steamrip.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 primaryUrl.IndexOf("imageban.ru", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 primaryUrl.IndexOf("capsule_231x87", StringComparison.OrdinalIgnoreCase) >= 0);
            if ((string.IsNullOrEmpty(primaryUrl) || isWeakOrHotlinkUrl) && hasSteamId)
            {
                fallbackUrl = primaryUrl;
                primaryUrl = "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/" + steamAppId + "/header.jpg";
            }
            if (string.IsNullOrEmpty(primaryUrl) && string.IsNullOrWhiteSpace(gp.Name))
            {
                if (onDone != null) onDone(false);
                return;
            }

            string cacheKey = (gp.Id ?? gp.SteamAppId ?? "img") + "_" + decodeWidth;
            lock (MemoryCache)
            {
                BitmapImage cached;
                if (MemoryCache.TryGetValue(cacheKey, out cached) && cached != null)
                {
                    targetBorder.Background = new ImageBrush(cached) { Stretch = Stretch.UniformToFill, Opacity = opacity };
                    if (onDone != null) onDone(true);
                    return;
                }
            }

            string safeFile = Regex.Replace(gp.Id ?? gp.SteamAppId ?? "game", @"[^a-zA-Z0-9_-]", "");
            if (string.IsNullOrEmpty(safeFile)) safeFile = "game";
            string localPath = Path.Combine(CacheDir, safeFile + ".jpg");

            // Diskten okuma/decode ve indirme tamamen arka planda yapılır
            ThreadPool.QueueUserWorkItem(delegate
            {
                BitmapImage bmp = LoadBitmapFromDisk(localPath, decodeWidth);
                if (bmp == null)
                {
                    bool shouldDownload = false;
                    lock (InFlight)
                    {
                        if (!InFlight.Contains(localPath))
                        {
                            InFlight.Add(localPath);
                            shouldDownload = true;
                        }
                    }

                    if (shouldDownload)
                    {
                        bool entered = false;
                        try
                        {
                            entered = DownloadGate.Wait(15000);
                            if (entered)
                            {
                                string tmpPath = localPath + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".tmp";
                                try
                                {
                                    using (TimeoutWebClient wc = new TimeoutWebClient(6000))
                                    {
                                        wc.Proxy = null;
                                        wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                                        bool ok = false;
                                        List<string> candidates = new List<string>();
                                        if (!string.IsNullOrEmpty(primaryUrl))
                                            candidates.Add(primaryUrl);
                                        if (!string.IsNullOrEmpty(fallbackUrl) && fallbackUrl != primaryUrl)
                                            candidates.Add(fallbackUrl);
                                        if (hasSteamId)
                                        {
                                            string s1 = "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/" + steamAppId + "/header.jpg";
                                            string s2 = "https://cdn.cloudflare.steamstatic.com/steam/apps/" + steamAppId + "/header.jpg";
                                            if (!candidates.Contains(s1)) candidates.Add(s1);
                                            if (!candidates.Contains(s2)) candidates.Add(s2);
                                        }

                                        foreach (string urlCand in candidates)
                                        {
                                            try
                                            {
                                                if (File.Exists(tmpPath)) File.Delete(tmpPath);
                                                Uri u;
                                                if (Uri.TryCreate(urlCand, UriKind.Absolute, out u) && urlCand.IndexOf("steamstatic.com", StringComparison.OrdinalIgnoreCase) < 0)
                                                    wc.Headers["Referer"] = u.Scheme + "://" + u.Host + "/";
                                                else
                                                    wc.Headers.Remove("Referer");
                                                wc.DownloadFile(urlCand, tmpPath);
                                                if (IsValidRasterFile(tmpPath))
                                                {
                                                    ok = true;
                                                    break;
                                                }
                                            }
                                            catch { ok = false; }
                                        }

                                        if (!ok && !string.IsNullOrWhiteSpace(gp.Name))
                                        {
                                            string steamFallback = ResolveSteamHeaderFallback(gp.Name);
                                            if (!string.IsNullOrEmpty(steamFallback))
                                            {
                                                try
                                                {
                                                    if (File.Exists(tmpPath)) File.Delete(tmpPath);
                                                    wc.Headers.Remove("Referer");
                                                    wc.DownloadFile(steamFallback, tmpPath);
                                                    if (IsValidRasterFile(tmpPath))
                                                    {
                                                        ok = true;
                                                        gp.BannerUrl = steamFallback;
                                                    }
                                                }
                                                catch { ok = false; }
                                            }
                                        }

                                        if (ok)
                                        {
                                            try
                                            {
                                                if (File.Exists(localPath)) File.Delete(localPath);
                                                File.Move(tmpPath, localPath);
                                            }
                                            catch { }
                                        }
                                    }
                                }
                                finally
                                {
                                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            if (entered) { try { DownloadGate.Release(); } catch { } }
                            lock (InFlight) { InFlight.Remove(localPath); }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < 15; i++)
                        {
                            Thread.Sleep(120);
                            if (File.Exists(localPath) && new FileInfo(localPath).Length > 1024) break;
                        }
                    }

                    bmp = LoadBitmapFromDisk(localPath, decodeWidth);
                }

                if (bmp != null)
                {
                    AddToMemoryCache(cacheKey, bmp);
                    try
                    {
                        if (targetBorder.Dispatcher.HasShutdownStarted) return;
                        targetBorder.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(delegate
                        {
                            try
                            {
                                targetBorder.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill, Opacity = opacity };
                                targetBorder.InvalidateVisual();
                                if (onDone != null) onDone(true);
                            }
                            catch { if (onDone != null) onDone(false); }
                        }));
                    }
                    catch { }
                }
                else if (onDone != null)
                {
                    try
                    {
                        if (!targetBorder.Dispatcher.HasShutdownStarted)
                            targetBorder.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(delegate { onDone(false); }));
                    }
                    catch { }
                }
            });
        }

        private static bool IsValidRasterFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                FileInfo fi = new FileInfo(path);
                if (fi.Length <= 1024) return false;
                byte[] hdr = new byte[4];
                using (FileStream fs = File.OpenRead(path))
                {
                    if (fs.Read(hdr, 0, 4) < 4) return false;
                }
                bool isJpg = (hdr[0] == 0xFF && hdr[1] == 0xD8);
                bool isPng = (hdr[0] == 0x89 && hdr[1] == 0x50 && hdr[2] == 0x4E && hdr[3] == 0x47);
                bool isGif = (hdr[0] == 0x47 && hdr[1] == 0x49 && hdr[2] == 0x46);
                bool isBmp = (hdr[0] == 0x42 && hdr[1] == 0x4D);
                return isJpg || isPng || isGif || isBmp;
            }
            catch { return false; }
        }

        private static BitmapImage LoadBitmapFromDisk(string path, int decodeWidth)
        {
            try
            {
                if (!IsValidRasterFile(path))
                {
                    try { if (File.Exists(path)) File.Delete(path); } catch { }
                    return null;
                }

                byte[] bytes = File.ReadAllBytes(path);
                if (bytes == null || bytes.Length <= 1024) return null;

                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    BitmapImage bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
                return null;
            }
        }

        /// <summary>
        /// Sonraki sayfa/scroll kartlarının kapaklarını arka planda sessizce diske ön-yükler.
        /// </summary>
        public static void PrefetchCovers(IList<GameProfile> list, int startIndex, int count)
        {
            if (list == null || startIndex < 0 || startIndex >= list.Count || count <= 0) return;
            int end = Math.Min(list.Count, startIndex + count);
            List<GameProfile> slice = new List<GameProfile>();
            for (int i = startIndex; i < end; i++)
            {
                if (list[i] != null) slice.Add(list[i]);
            }
            if (slice.Count == 0) return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (GameProfile gp in slice)
                {
                    try
                    {
                        string url = IsUsableImageUrl(gp.BannerUrl) ? gp.BannerUrl.Trim() : (IsUsableImageUrl(gp.CapsuleUrl) ? gp.CapsuleUrl.Trim() : null);
                        if (string.IsNullOrEmpty(url))
                        {
                            string sid = (gp.SteamAppId ?? "").Trim();
                            if (!string.IsNullOrEmpty(sid) && sid != "480" && Regex.IsMatch(sid, @"^\d{2,8}$"))
                                url = "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/" + sid + "/header.jpg";
                        }
                        if (string.IsNullOrEmpty(url)) continue;

                        string safeFile = Regex.Replace(gp.Id ?? gp.SteamAppId ?? "game", @"[^a-zA-Z0-9_-]", "");
                        if (string.IsNullOrEmpty(safeFile)) safeFile = "game";
                        string localPath = Path.Combine(CacheDir, safeFile + ".jpg");
                        if (File.Exists(localPath) && new FileInfo(localPath).Length > 1024) continue;

                        bool entered = false;
                        try
                        {
                            entered = DownloadGate.Wait(5000);
                            if (!entered) continue;
                            string tmpPath = localPath + ".pre.tmp";
                            using (TimeoutWebClient wc = new TimeoutWebClient(5000))
                            {
                                wc.Proxy = null;
                                wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                                wc.DownloadFile(url, tmpPath);
                                if (IsValidRasterFile(tmpPath))
                                {
                                    if (File.Exists(localPath)) File.Delete(localPath);
                                    File.Move(tmpPath, localPath);
                                }
                                else
                                {
                                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                                }
                            }
                        }
                        catch { }
                        finally { if (entered) { try { DownloadGate.Release(); } catch { } } }
                    }
                    catch { }
                }
            });
        }
    }
}
