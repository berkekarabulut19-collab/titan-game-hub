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
    /// Steam vitrin afiş çözücü (optimize edilmiş).
    ///
    /// Canlı katalogdaki oyunların bir kısmı afiş URL'si vermiyor (
    /// kayıtları gibi). Bu sınıf Steam'in halka açık mağaza arama API'sinden oyunu bulup
    /// resmi header.jpg adresini üretir.
    ///
    /// Optimizasyonlar:
    ///  - eşzamanlı istek sınırı (4) → mağaza açılışında ağ boğulmaz
    ///  - bellek + disk önbelleği (negatif sonuçlar dahil) → aynı oyun tekrar sorgulanmaz
    ///  - isim benzerliği eşiği → yanlış oyunun kapağı basılmaz
    /// </summary>
    public static class SteamArtworkResolver
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _inFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim _throttle = new SemaphoreSlim(16, 16);
        private static bool _diskLoaded = false;

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

        static SteamArtworkResolver()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { }
        }

        private static string CacheFile
        {
            get
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "artwork_cache.txt");
            }
        }

        private static void EnsureDiskCacheLoaded()
        {
            if (_diskLoaded) return;
            lock (_lock)
            {
                if (_diskLoaded) return;
                _diskLoaded = true;
                try
                {
                    string f = CacheFile;
                    if (!File.Exists(f)) return;
                    foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
                    {
                        int sep = line.IndexOf('\t');
                        if (sep <= 0) continue;
                        string key = line.Substring(0, sep);
                        string val = line.Substring(sep + 1);
                        if (!_cache.ContainsKey(key)) _cache[key] = val;
                    }
                }
                catch { }
            }
        }

        private static void RemoveFromDiskCache(string key)
        {
            try
            {
                string f = CacheFile;
                if (!File.Exists(f)) return;
                List<string> keep = new List<string>();
                foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
                {
                    int sep = line.IndexOf('	');
                    if (sep > 0 && string.Equals(line.Substring(0, sep), key, StringComparison.OrdinalIgnoreCase)) continue;
                    keep.Add(line);
                }
                File.WriteAllLines(f, keep.ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        private static void PersistEntry(string key, string value)
        {
            try
            {
                lock (_lock) { File.AppendAllText(CacheFile, key + "\t" + value + Environment.NewLine, Encoding.UTF8); }
            }
            catch { }
        }

        public static string GetCached(string gameName)
        {
            if (string.IsNullOrEmpty(gameName)) return null;
            EnsureDiskCacheLoaded();
            lock (_lock)
            {
                string url;
                return _cache.TryGetValue(gameName.Trim(), out url) ? url : null;
            }
        }

        public static void ResolveAsync(string gameName, Action<string> onDone)
        {
            ResolveAsync(gameName, onDone, false);
        }

        /// <summary>force = true ise negatif/bayat önbellek yok sayılır (kapak tamamlama için).</summary>
        public static void ResolveAsync(string gameName, Action<string> onDone, bool force)
        {
            if (string.IsNullOrEmpty(gameName) || onDone == null) return;

            string key = gameName.Trim();
            if (force)
            {
                // Disk önbelleğindeki negatif kaydı da sil (yoksa tekrar "yok" sayılıyordu)
                EnsureDiskCacheLoaded();
                lock (_lock)
                {
                    _cache.Remove(key);
                    _inFlight.Remove(key);
                }
                RemoveFromDiskCache(key);
            }

            string cached = GetCached(key);
            if (cached != null)
            {
                onDone(cached.Length == 0 ? null : cached);
                return;
            }

            lock (_lock)
            {
                if (!_inFlight.Add(key)) return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                string result = null;
                bool entered = false;
                try
                {
                    entered = _throttle.Wait(20000);
                    if (entered)
                    {
                        result = QuerySteamStore(key);
                    }
                }
                catch { }
                finally
                {
                    if (entered) { try { _throttle.Release(); } catch { } }
                }

                lock (_lock)
                {
                    if (!string.IsNullOrEmpty(result))
                        _cache[key] = result;
                    _inFlight.Remove(key);
                }
                if (!string.IsNullOrEmpty(result)) PersistEntry(key, result);

                try { onDone(result); }
                catch { }
            });
        }

        /// <summary>Steam mağaza aramasında tüm sonuçları gezip isim benzerliğine göre seçer; gerekirse kısa başlıkla fallback yapar.</summary>
        private static string QuerySteamStore(string term)
        {
            string clean = CleanTitle(term);
            if (clean.Length < 2) return null;

            string art = SearchAndPickArtwork(clean);
            if (!string.IsNullOrEmpty(art)) return art;

            string[] words = clean.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 3)
            {
                string short3 = string.Join(" ", words, 0, Math.Min(3, words.Length));
                if (!short3.Equals(clean, StringComparison.OrdinalIgnoreCase))
                {
                    art = SearchAndPickArtwork(short3);
                    if (!string.IsNullOrEmpty(art)) return art;
                }
            }
            if (words.Length >= 2)
            {
                string short2 = words[0] + " " + words[1];
                if (!short2.Equals(clean, StringComparison.OrdinalIgnoreCase))
                {
                    art = SearchAndPickArtwork(short2);
                    if (!string.IsNullOrEmpty(art)) return art;
                }
            }

            // 2. İnternet Kaynağı: CheapShark Oyun API (SteamAppID veya thumb)
            art = QueryCheapSharkArtwork(clean);
            if (!string.IsNullOrEmpty(art)) return art;

            // 3. İnternet Kaynağı: Wikipedia OpenSearch + Summary kapak görseli
            art = QueryWikipediaArtwork(clean);
            if (!string.IsNullOrEmpty(art)) return art;

            return null;
        }

        private static string QueryCheapSharkArtwork(string clean)
        {
            try
            {
                string json;
                using (TimeoutWebClient wc = new TimeoutWebClient(6000))
                {
                    wc.Proxy = null;
                    wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    wc.Encoding = Encoding.UTF8;
                    json = wc.DownloadString("https://www.cheapshark.com/api/1.0/games?title=" + Uri.EscapeDataString(clean) + "&limit=5");
                }
                System.Web.Script.Serialization.JavaScriptSerializer ser =
                    new System.Web.Script.Serialization.JavaScriptSerializer();
                object[] arr = ser.DeserializeObject(json) as object[];
                if (arr == null || arr.Length == 0) return null;

                string wanted = Normalize(clean);
                string bestThumb = null;
                string bestSteamId = null;
                double bestScore = 0;

                foreach (object o in arr)
                {
                    Dictionary<string, object> item = o as Dictionary<string, object>;
                    if (item == null) continue;
                    string external = item.ContainsKey("external") ? Convert.ToString(item["external"]) : null;
                    string steamAppId = item.ContainsKey("steamAppID") ? Convert.ToString(item["steamAppID"]) : null;
                    string thumb = item.ContainsKey("thumb") ? Convert.ToString(item["thumb"]) : null;
                    if (string.IsNullOrEmpty(external)) continue;

                    double score = Similarity(wanted, Normalize(external));
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestSteamId = steamAppId;
                        bestThumb = thumb;
                    }
                }

                if (bestScore >= 0.42)
                {
                    if (!string.IsNullOrEmpty(bestSteamId))
                    {
                        string hdr = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/" + bestSteamId + "/header.jpg";
                        if (UrlExists(hdr)) return hdr;
                        string hdr2 = "https://cdn.akamai.steamstatic.com/steam/apps/" + bestSteamId + "/header.jpg";
                        if (UrlExists(hdr2)) return hdr2;
                    }
                    if (!string.IsNullOrEmpty(bestThumb) && bestThumb.StartsWith("http")) return bestThumb;
                }
            }
            catch { }
            return null;
        }

        private static string QueryWikipediaArtwork(string clean)
        {
            try
            {
                string searchJson;
                using (TimeoutWebClient wc = new TimeoutWebClient(6000))
                {
                    wc.Proxy = null;
                    wc.Headers.Add("User-Agent", "TitanGameHub/9.4 (Desktop)");
                    wc.Encoding = Encoding.UTF8;
                    searchJson = wc.DownloadString("https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch=" +
                        Uri.EscapeDataString(clean + " video game") + "&format=json&srlimit=3");
                }
                System.Web.Script.Serialization.JavaScriptSerializer ser =
                    new System.Web.Script.Serialization.JavaScriptSerializer();
                Dictionary<string, object> root = ser.DeserializeObject(searchJson) as Dictionary<string, object>;
                if (root == null || !root.ContainsKey("query")) return null;
                Dictionary<string, object> queryObj = root["query"] as Dictionary<string, object>;
                if (queryObj == null || !queryObj.ContainsKey("search")) return null;
                object[] results = queryObj["search"] as object[];
                if (results == null || results.Length == 0) return null;

                string wanted = Normalize(clean);
                foreach (object o in results)
                {
                    Dictionary<string, object> hit = o as Dictionary<string, object>;
                    if (hit == null || !hit.ContainsKey("title")) continue;
                    string pageTitle = Convert.ToString(hit["title"]);
                    if (string.IsNullOrEmpty(pageTitle)) continue;
                    string strippedTitle = Regex.Replace(pageTitle, @"\s*\([^\)]*\)", "").Trim();
                    if (Similarity(wanted, Normalize(strippedTitle)) < 0.40) continue;

                    using (TimeoutWebClient wc2 = new TimeoutWebClient(6000))
                    {
                        wc2.Proxy = null;
                        wc2.Headers.Add("User-Agent", "TitanGameHub/9.4 (Desktop)");
                        wc2.Encoding = Encoding.UTF8;
                        string sumJson = wc2.DownloadString("https://en.wikipedia.org/api/rest_v1/page/summary/" + Uri.EscapeDataString(pageTitle.Replace(' ', '_')));
                        Dictionary<string, object> sum = ser.DeserializeObject(sumJson) as Dictionary<string, object>;
                        if (sum == null) continue;
                        if (sum.ContainsKey("originalimage"))
                        {
                            Dictionary<string, object> orig = sum["originalimage"] as Dictionary<string, object>;
                            if (orig != null && orig.ContainsKey("source"))
                            {
                                string src = Convert.ToString(orig["source"]);
                                if (!string.IsNullOrEmpty(src) && src.StartsWith("http")) return src;
                            }
                        }
                        if (sum.ContainsKey("thumbnail"))
                        {
                            Dictionary<string, object> th = sum["thumbnail"] as Dictionary<string, object>;
                            if (th != null && th.ContainsKey("source"))
                            {
                                string src = Convert.ToString(th["source"]);
                                if (!string.IsNullOrEmpty(src) && src.StartsWith("http")) return src;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static string SearchAndPickArtwork(string clean)
        {
            string json;
            using (TimeoutWebClient wc = new TimeoutWebClient(8000))
            {
                wc.Proxy = null;
                wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                wc.Encoding = Encoding.UTF8;
                json = wc.DownloadString("https://store.steampowered.com/api/storesearch/?term=" +
                                         Uri.EscapeDataString(clean) + "&l=english&cc=us");
            }

            string wanted = Normalize(clean);
            string bestId = null;
            string bestTinyImage = null;
            double bestScore = 0;

            try
            {
                System.Web.Script.Serialization.JavaScriptSerializer ser =
                    new System.Web.Script.Serialization.JavaScriptSerializer();
                Dictionary<string, object> root = ser.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) return null;

                object itemsObj;
                if (!root.TryGetValue("items", out itemsObj) || itemsObj == null) return null;

                object[] items = itemsObj as object[];
                if (items == null) return null;

                foreach (object o in items)
                {
                    Dictionary<string, object> item = o as Dictionary<string, object>;
                    if (item == null) continue;

                    string name = item.ContainsKey("name") ? Convert.ToString(item["name"]) : null;
                    string id = item.ContainsKey("id") ? Convert.ToString(item["id"]) : null;
                    string tiny = item.ContainsKey("tiny_image") ? Convert.ToString(item["tiny_image"]) : null;
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id)) continue;

                    double score = Similarity(wanted, Normalize(name));
                    if (score > bestScore) { bestScore = score; bestId = id; bestTinyImage = tiny; }
                }
            }
            catch { return null; }

            if (bestId != null && bestScore >= 0.38)
            {
                // 1) appdetails: en doğru görsel (header_image)
                string detailsArt = QueryAppDetailsArtwork(bestId);
                if (!string.IsNullOrEmpty(detailsArt)) return detailsArt;

                List<string> candidates = new List<string>();

                // Yeni Steam CDN şeması (2024+ oyunlar): store_item_assets
                candidates.Add("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/" + bestId + "/header.jpg");
                candidates.Add("https://cdn.akamai.steamstatic.com/steam/apps/" + bestId + "/header.jpg");
                candidates.Add("https://cdn.akamai.steamstatic.com/steam/apps/" + bestId + "/capsule_616x353.jpg");
                candidates.Add("https://cdn.akamai.steamstatic.com/steam/apps/" + bestId + "/library_600x900.jpg");

                // tiny_image yolundan türet: .../apps/<id>/<hash>/capsule_231x87.jpg → aynı klasörde header.jpg
                if (!string.IsNullOrEmpty(bestTinyImage))
                {
                    string baseDir = bestTinyImage;
                    int slash = baseDir.LastIndexOf('/');
                    if (slash > 0)
                    {
                        baseDir = baseDir.Substring(0, slash + 1);
                        candidates.Add(baseDir + "header.jpg");
                        candidates.Add(baseDir + "library_600x900.jpg");
                    }
                    candidates.Add(bestTinyImage);   // son çare: düşük çözünürlüklü gerçek kapak
                }

                foreach (string c in candidates)
                    if (UrlExists(c)) return c;

                if (!string.IsNullOrEmpty(bestTinyImage)) return bestTinyImage;
            }
            return null;
        }

        /// <summary>appdetails API'den header_image / capsule_image çeker (yeni CDN şeması için en güvenilir yol).</summary>
        private static string QueryAppDetailsArtwork(string appId)
        {
            try
            {
                string json;
                using (TimeoutWebClient wc = new TimeoutWebClient(8000))
                {
                    wc.Proxy = null;
                    wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    wc.Encoding = Encoding.UTF8;
                    json = wc.DownloadString("https://store.steampowered.com/api/appdetails?appids=" + appId + "&l=english&filters=basic");
                }

                System.Web.Script.Serialization.JavaScriptSerializer ser =
                    new System.Web.Script.Serialization.JavaScriptSerializer();
                Dictionary<string, object> root = ser.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) return null;

                object appNode;
                if (!root.TryGetValue(appId, out appNode) || appNode == null) return null;

                Dictionary<string, object> appDict = appNode as Dictionary<string, object>;
                if (appDict == null) return null;

                object dataNode;
                if (!appDict.TryGetValue("data", out dataNode) || dataNode == null) return null;

                Dictionary<string, object> data = dataNode as Dictionary<string, object>;
                if (data == null) return null;

                object headerObj;
                if (data.TryGetValue("header_image", out headerObj) && headerObj != null)
                {
                    string header = headerObj.ToString();
                    if (!string.IsNullOrEmpty(header) && header.StartsWith("http")) return header;
                }

                object capsuleObj;
                if (data.TryGetValue("capsule_image", out capsuleObj) && capsuleObj != null)
                {
                    string capsule = capsuleObj.ToString();
                    if (!string.IsNullOrEmpty(capsule) && capsule.StartsWith("http")) return capsule;
                }
            }
            catch { }
            return null;
        }

        private static string CleanTitle(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string s = raw;
            s = Regex.Replace(s, @"\[[^\]]*\]|\([^\)]*\)", " ");
            s = Regex.Replace(s, @"(?i)\b(free download|pre-?installed|repack|fitgirl|elamigos|tenoke|steamrip|ovagames|freegog|crotorrents|online-?fix|all dlcs?|dlcs?|bonuses|bonus|deluxe|ultimate|complete|definitive|remastered|goty|gold|digital|edition|multi\d+|build\s*\d+|update|hotfix|portable|cracked|v?\d+(\.\d+)+)\b", " ");
            s = Regex.Replace(s, @"[\u2013\u2014\-_:+]+", " ");
            s = Regex.Replace(s, @"[^A-Za-z0-9 ]", " ");
            s = Regex.Replace(s, @"\s{2,}", " ").Trim(' ', ':', '.');
            return s;
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]", "");
        }

        private static double Similarity(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return 0;
            if (a == b) return 1.0;
            if (a.Contains(b) || b.Contains(a))
                return 0.85 * ((double)Math.Min(a.Length, b.Length) / Math.Max(a.Length, b.Length)) + 0.15;

            int dist = Levenshtein(a, b);
            return 1.0 - ((double)dist / Math.Max(a.Length, b.Length));
        }

        private static int Levenshtein(string a, string b)
        {
            if (a == b) return 0;
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;
            int[] prev = new int[b.Length + 1];
            int[] cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                    int del = prev[j] + 1, ins = cur[j - 1] + 1, sub = prev[j - 1] + cost;
                    cur[j] = Math.Min(Math.Min(del, ins), sub);
                }
                int[] t = prev; prev = cur; cur = t;
            }
            return prev[b.Length];
        }

        private static bool UrlExists(string url)
        {
            // HEAD bazı CDN'lerde 403 dönüyor; hızlı tek Range'li GET kullan.
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Proxy = null;
                req.Timeout = 4000;
                req.ReadWriteTimeout = 4000;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                req.AddRange(0, 2047);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    return (code == 200 || code == 206);
                }
            }
            catch { return false; }
        }
    }
}
