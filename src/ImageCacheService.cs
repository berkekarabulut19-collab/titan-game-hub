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
        private static readonly SemaphoreSlim DownloadGate = new SemaphoreSlim(6, 6);

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
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
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

            string primaryUrl = !string.IsNullOrEmpty(gp.BannerUrl) ? gp.BannerUrl : gp.CapsuleUrl;
            string fallbackUrl = !string.IsNullOrEmpty(gp.CapsuleUrl) ? gp.CapsuleUrl : gp.BannerUrl;
            if (string.IsNullOrEmpty(primaryUrl))
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
                            entered = DownloadGate.Wait(8000);
                            if (entered)
                            {
                                string tmpPath = localPath + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".tmp";
                                try
                                {
                                    using (TimeoutWebClient wc = new TimeoutWebClient(8000))
                                    {
                                        wc.Proxy = null;
                                        wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                                        bool ok = false;
                                        try
                                        {
                                            wc.DownloadFile(primaryUrl, tmpPath);
                                            ok = File.Exists(tmpPath) && new FileInfo(tmpPath).Length > 1024;
                                        }
                                        catch { ok = false; }

                                        if (!ok && !string.IsNullOrEmpty(fallbackUrl) && fallbackUrl != primaryUrl)
                                        {
                                            try
                                            {
                                                wc.DownloadFile(fallbackUrl, tmpPath);
                                                ok = File.Exists(tmpPath) && new FileInfo(tmpPath).Length > 1024;
                                            }
                                            catch { ok = false; }
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
                        targetBorder.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                        {
                            try
                            {
                                targetBorder.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill, Opacity = opacity };
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
                            targetBorder.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { onDone(false); }));
                    }
                    catch { }
                }
            });
        }

        private static BitmapImage LoadBitmapFromDisk(string path, int decodeWidth)
        {
            try
            {
                if (!File.Exists(path)) return null;
                FileInfo fi = new FileInfo(path);
                if (fi.Length <= 1024) return null;

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
                return null;
            }
        }
    }
}
