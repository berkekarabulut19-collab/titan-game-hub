using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TitanGameHub
{
    /// <summary>
    /// Magnet/torrent kataloğu: her oyun için sayfadan çıkarılan magnet veya .torrent bağlantısı
    /// data/magnets.txt içinde saklanır (satır: oyunId|magnetVeyaUrl).
    /// Böylece 403 veren hostlarda uygulama doğrudan aria2 ile indirebilir.
    /// </summary>
    public static class MagnetCatalog
    {
        private static readonly object Lock = new object();

        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "magnets.txt");
            }
        }

        private static Dictionary<string, string> Load()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string f = FilePath;
                if (!File.Exists(f)) return map;
                foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    int sep = line.IndexOf('|');
                    if (sep <= 0) continue;
                    map[line.Substring(0, sep)] = line.Substring(sep + 1).Trim();
                }
            }
            catch { }
            return map;
        }

        public static string Get(GameProfile gp)
        {
            if (gp == null) return null;
            lock (Lock)
            {
                Dictionary<string, string> map = Load();
                string v;
                if (!string.IsNullOrEmpty(gp.Id) && map.TryGetValue(gp.Id, out v)) return v;
                if (!string.IsNullOrEmpty(gp.Name) && map.TryGetValue("name:" + gp.Name, out v)) return v;
                return null;
            }
        }

        public static void Put(GameProfile gp, string magnetOrUrl)
        {
            if (gp == null || string.IsNullOrEmpty(magnetOrUrl)) return;
            lock (Lock)
            {
                try
                {
                    Dictionary<string, string> map = Load();
                    string key = !string.IsNullOrEmpty(gp.Id) ? gp.Id : ("name:" + gp.Name);
                    map[key] = magnetOrUrl;

                    List<string> lines = new List<string>();
                    lines.Add("# TitanGameHub magnet kataloğu (oyunId|magnet)");
                    foreach (KeyValuePair<string, string> kv in map) lines.Add(kv.Key + "|" + kv.Value);
                    File.WriteAllLines(FilePath, lines.ToArray(), Encoding.UTF8);
                }
                catch { }
            }
        }

        /// <summary>Sayfadan magnet çıkarır ve kataloğa yazar.</summary>
        public static string ResolveAndRemember(GameProfile gp)
        {
            if (gp == null) return null;

            string existing = Get(gp);
            if (!string.IsNullOrEmpty(existing)) return existing;

            try
            {
                if (!string.IsNullOrEmpty(gp.DirectMirrorUrl))
                {
                    string u = gp.DirectMirrorUrl.Trim();
                    if (u.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) || u.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                    {
                        Put(gp, u);
                        return u;
                    }
                }

                // 2) online-fix.me: torrent yayınlayan kaynak
                string of = TryOnlineFix(gp);
                if (!string.IsNullOrEmpty(of))
                {
                    Put(gp, of);
                    return of;
                }

                // 3) Oyunun kendi sayfası (bazı siteler magnet gömüyor)
                if (!string.IsNullOrEmpty(gp.DownloadUrl) && gp.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    using (WebClient wc = new WebClient())
                    {
                        wc.Proxy = null;
                        wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                        string html = wc.DownloadString(gp.DownloadUrl);
                        string magnet = TorrentDownloadService.ExtractMagnetOrTorrent(html, gp.DownloadUrl);
                        if (!string.IsNullOrEmpty(magnet))
                        {
                            Put(gp, magnet);
                            return magnet;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>online-fix.me üzerinden magnet/.torrent bulmayı dener (torrent yayınlayan kaynak).</summary>
        private static string TryOnlineFix(GameProfile gp)
        {
            try
            {
                if (gp == null || string.IsNullOrEmpty(gp.Name)) return null;

                string searchUrl = "https://online-fix.me/index.php?do=search&subaction=search&story=" +
                                   Uri.EscapeDataString(gp.Name);

                string html;
                using (WebClient wc = new WebClient())
                {
                    wc.Proxy = null;
                    wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    html = wc.DownloadString(searchUrl);
                }

                Match link = Regex.Match(html, @"href=""(https://online-fix\.me/[^""]+)""", RegexOptions.IgnoreCase);
                if (!link.Success) return null;

                string pageUrl = link.Groups[1].Value;
                string page;
                using (WebClient wc2 = new WebClient())
                {
                    wc2.Proxy = null;
                    wc2.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    page = wc2.DownloadString(pageUrl);
                }

                return TorrentDownloadService.ExtractMagnetOrTorrent(page, pageUrl);
            }
            catch { return null; }
        }

        /// <summary>Katalogdaki oyunlar için magnet taraması (toplu).</summary>
        public static int BuildForCatalog(List<GameProfile> catalog, int limit, Action<string, int> progress)
        {
            if (catalog == null) return 0;

            int found = 0;
            int done = 0;
            int max = limit > 0 ? Math.Min(limit, catalog.Count) : catalog.Count;

            for (int i = 0; i < max; i++)
            {
                GameProfile gp = catalog[i];
                if (gp == null) continue;

                if (progress != null)
                    progress(string.Format("Magnet aranıyor: {0} ({1}/{2})", gp.Name, i + 1, max), (int)((i * 100.0) / Math.Max(1, max)));

                if (!string.IsNullOrEmpty(Get(gp))) { done++; continue; }

                string m = ResolveAndRemember(gp);
                if (!string.IsNullOrEmpty(m)) found++;
                done++;

                Thread.Sleep(200);
            }

            if (progress != null) progress("Magnet taraması bitti.", 100);
            return found;
        }

        public static int Count()
        {
            lock (Lock) { return Load().Count; }
        }
    }
}
