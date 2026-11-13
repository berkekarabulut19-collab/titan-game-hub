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

        static ImageCacheService()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            try
            {
                if (!Directory.Exists(CacheDir))
                    Directory.CreateDirectory(CacheDir);

                // Background cache maintenance: purge corrupt 0-byte files or entries older than 30 days
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
                                {
                                    fi.Delete();
                                }
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
        /// onDone(true/false): görsel yüklendi mi? Vitrin kartları yüklenemeyen afişler için
        /// yer tutucu (gradient + oyun adı) gösterir, siyah kutu bırakmaz.
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

            string safeFile = Regex.Replace(gp.Id ?? gp.SteamAppId ?? "game", @"[^a-zA-Z0-9_-]", "") + ".jpg";
            string localPath = Path.Combine(CacheDir, safeFile);

            if (File.Exists(localPath) && new FileInfo(localPath).Length > 1024)
            {
                BitmapImage bmp = LoadBitmapFromDisk(localPath, decodeWidth);
                if (bmp != null)
                {
                    AddToMemoryCache(cacheKey, bmp);
                    targetBorder.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill, Opacity = opacity };
                    if (onDone != null) onDone(true);
                    return;
                }
            }

            // Download asynchronously to local disk cache
            ThreadPool.QueueUserWorkItem(delegate
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
                    try
                    {
                        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                        DownloadGate.Wait(15000);
                        using (WebClient wc = new WebClient())
                        {
                            wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                            try
                            {
                                wc.DownloadFile(primaryUrl, localPath);
                            }
                            catch
                            {
                                if (!string.IsNullOrEmpty(fallbackUrl) && fallbackUrl != primaryUrl)
                                {
                                    try { wc.DownloadFile(fallbackUrl, localPath); } catch { }
                                }
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        try { DownloadGate.Release(); } catch { }
                        lock (InFlight) { InFlight.Remove(localPath); }
                    }
                }
                else
                {
                    // Wait briefly for in-flight download
                    for (int i = 0; i < 15; i++)
                    {
                        Thread.Sleep(120);
                        if (File.Exists(localPath) && new FileInfo(localPath).Length > 1024) break;
                    }
                }

                if (File.Exists(localPath) && new FileInfo(localPath).Length > 1024)
                {
                    targetBorder.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try
                        {
                            BitmapImage bmp = LoadBitmapFromDisk(localPath, decodeWidth);
                            if (bmp != null)
                            {
                                AddToMemoryCache(cacheKey, bmp);
                                targetBorder.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill, Opacity = opacity };
                                if (onDone != null) onDone(true);
                            }
                            else if (onDone != null) onDone(false);
                        }
                        catch { if (onDone != null) onDone(false); }
                    }));
                }
                else
                {
                    if (onDone != null)
                        targetBorder.Dispatcher.BeginInvoke(new Action(delegate { onDone(false); }));
                }
            });
        }

        private static BitmapImage LoadBitmapFromDisk(string path, int decodeWidth)
        {
            try
            {
                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }
}
