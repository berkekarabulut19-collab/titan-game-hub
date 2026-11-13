using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace TitanGameHub
{
    /// <summary>
    /// Çoklu kaynak sürüm çözücü.
    ///
    /// Ölçüm sonucu: hiçbir site tek başına tüm kataloğu kapsamıyor ve güncelleme hızları farklı
    /// (SteamRIP 30/34 kapsama ama bazı sayfalar aylarca bayat; FitGirl/OvaGames ~1-3 gün ama
    /// kapsama düşük). Bu yüzden güncelleme kontrolü tek siteye
    /// değil, birkaç kaynağa bakıp EN YENİ sürümü seçmeye dayanmalı.
    /// </summary>
    public static class MultiSourceVersionResolver
    {
        public class Result
        {
            public string SourceName;
            public string Version;
            public string PostUrl;
            public string DirectUrl;      // o kaynağın verdiği en iyi doğrudan arşiv linki
            public string HostLabel;     // "pixeldrain.com", "fileditchfiles.st" ...
            public bool IsNewerThanLocal;
        }

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, Result> _cache = new Dictionary<string, Result>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> _cacheTime = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _inFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

        private static readonly string[] Sources = new string[]
        {
            "https://www.ovagames.com",
            "https://fitgirl-repacks.site"
        };
        private static readonly string[] SourceNames = new string[] { "OvaGames", "FitGirl" };

        public static Result GetCached(string gameName)
        {
            if (string.IsNullOrEmpty(gameName)) return null;
            lock (_lock)
            {
                DateTime ts;
                Result r;
                if (_cache.TryGetValue(gameName, out r) && _cacheTime.TryGetValue(gameName, out ts) &&
                    (DateTime.UtcNow - ts) < CacheTtl)
                    return r;
                return null;
            }
        }

        /// <summary>Arka planda tüm kaynaklara bakar, en yeni sürümü döndürür.</summary>
        public static void ResolveAsync(string gameName, string localVersion, Action<Result> onDone)
        {
            if (string.IsNullOrEmpty(gameName) || onDone == null) return;

            Result cached = GetCached(gameName);
            if (cached != null)
            {
                onDone(cached);
                return;
            }

            lock (_lock)
            {
                if (!_inFlight.Add(gameName)) return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                Result best = new Result();
                try
                {
                    for (int i = 0; i < Sources.Length; i++)
                    {
                        string version;
                        string link;
                        string direct;
                        if (TryQuerySource(Sources[i], gameName, out version, out link, out direct))
                        {
                            if (string.IsNullOrEmpty(best.Version) || CompareVersions(version, best.Version) > 0)
                            {
                                best.Version = version;
                                best.SourceName = SourceNames[i];
                                best.PostUrl = link;
                                best.DirectUrl = direct;
                                best.HostLabel = string.IsNullOrEmpty(direct) ? null : DownloadHostPolicy.HostLabel(direct);
                            }
                        }
                        Thread.Sleep(120);
                    }
                }
                catch { }

                if (!string.IsNullOrEmpty(best.Version))
                    best.IsNewerThanLocal = CompareVersions(best.Version, localVersion) > 0;

                lock (_lock)
                {
                    _cache[gameName] = best;
                    _cacheTime[gameName] = DateTime.UtcNow;
                    _inFlight.Remove(gameName);
                }

                try { onDone(best); }
                catch { }
            });
        }

        /// <summary>Senkron varyant (arka plan tarayıcısından çağrılır).</summary>
        public static Result ResolveSync(string gameName, string localVersion)
        {
            if (string.IsNullOrEmpty(gameName)) return null;

            Result cached = GetCached(gameName);
            if (cached != null) return cached;

            Result best = new Result();
            try
            {
                localVersionHint = localVersion;
                for (int i = 0; i < Sources.Length; i++)
                {
                    string version;
                    string link;
                    string direct;
                    if (TryQuerySource(Sources[i], gameName, out version, out link, out direct))
                    {
                        if (string.IsNullOrEmpty(best.Version) || CompareVersions(version, best.Version) > 0)
                        {
                            best.Version = version;
                            best.SourceName = SourceNames[i];
                            best.PostUrl = link;
                            best.DirectUrl = direct;
                            best.HostLabel = string.IsNullOrEmpty(direct) ? null : DownloadHostPolicy.HostLabel(direct);
                        }
                    }
                    Thread.Sleep(80);
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(best.Version))
                best.IsNewerThanLocal = CompareVersions(best.Version, localVersion) > 0;

            lock (_lock)
            {
                _cache[gameName] = best;
                _cacheTime[gameName] = DateTime.UtcNow;
            }
            return best;
        }

        private static bool TryQuerySource(string baseUrl, string gameName, out string version, out string link, out string directUrl)
        {
            version = null;
            link = null;
            directUrl = null;

            string clean = Regex.Replace(gameName, @"[^A-Za-z0-9 ]", " ").Trim();
            string[] terms = (clean.Length >= 3 && !string.Equals(clean, gameName, StringComparison.OrdinalIgnoreCase))
                ? new string[] { gameName, clean }
                : new string[] { gameName };

            for (int t = 0; t < terms.Length; t++)
            {
                if (TryQueryTerm(baseUrl, terms[t], gameName, out version, out link, out directUrl))
                    return true;
                Thread.Sleep(60);
            }
            return false;
        }

        [ThreadStatic]
        private static string localVersionHint;

        private static bool TryQueryTerm(string baseUrl, string term, string gameName, out string version, out string link, out string directUrl)
        {
            version = null;
            link = null;
            directUrl = null;
            try
            {
                string api = baseUrl.TrimEnd('/') + "/wp-json/wp/v2/posts?search=" +
                             Uri.EscapeDataString(term) +
                             "&per_page=20&orderby=relevance&_fields=title,link,content";

                string json;
                using (WebClient wc = new WebClient())
                {
                    wc.Proxy = null;
                    wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    wc.Encoding = System.Text.Encoding.UTF8;
                    json = wc.DownloadString(api);
                }

                System.Web.Script.Serialization.JavaScriptSerializer ser =
                    new System.Web.Script.Serialization.JavaScriptSerializer();
                object[] posts = ser.DeserializeObject(json) as object[];
                if (posts == null) return false;

                string nameKey = Normalize(gameName);

                foreach (object o in posts)
                {
                    System.Collections.Generic.Dictionary<string, object> post =
                        o as System.Collections.Generic.Dictionary<string, object>;
                    if (post == null) continue;

                    string title = GetRendered(post, "title");
                    if (string.IsNullOrEmpty(title)) continue;

                    string tKey = Normalize(title);
                    if (tKey.IndexOf(nameKey, StringComparison.Ordinal) < 0 && nameKey.IndexOf(tKey, StringComparison.Ordinal) < 0)
                        continue;

                    string v = ExtractVersion(title);
                    if (string.IsNullOrEmpty(v)) continue;

                    version = v;

                    object linkObj;
                    if (post.TryGetValue("link", out linkObj) && linkObj != null)
                        link = linkObj.ToString();

                    string body = GetRendered(post, "content");
                    if (!string.IsNullOrEmpty(body))
                    {
                        bool updating = !string.IsNullOrEmpty(gameName) && !string.IsNullOrEmpty(localVersionHint);
                        directUrl = PickBestArchiveLink(body, updating);
                    }

                    return true;
                }
            }
            catch { }
            return false;
        }

        private static string GetRendered(System.Collections.Generic.Dictionary<string, object> post, string field)
        {
            try
            {
                object node;
                if (!post.TryGetValue(field, out node) || node == null) return null;

                System.Collections.Generic.Dictionary<string, object> dict =
                    node as System.Collections.Generic.Dictionary<string, object>;
                if (dict != null)
                {
                    object rendered;
                    if (dict.TryGetValue("rendered", out rendered) && rendered != null)
                        return rendered.ToString();
                    return null;
                }
                return node.ToString();
            }
            catch { return null; }
        }

        /// <summary>Sayfa HTML'inden arşiv linklerini çıkarır; host hız puanına göre en iyisini döndürür.</summary>
        public static string PickBestArchiveLink(string html)
        {
            return PickBestArchiveLink(html, false);
        }

        /// <summary>
        /// preferUpdate = true ise "…update.../...patch.../...vX.Y-elamigos..." içeren arşiv linkleri
        /// tam repack linklerinden önce değerlendirilir (OvaGames/ElAmigos yama paketleri).
        /// </summary>
        public static string PickBestArchiveLink(string html, bool preferUpdate)
        {
            if (string.IsNullOrEmpty(html)) return null;

            MatchCollection m = Regex.Matches(html,
                @"((?:https?:)?//(?:[a-z0-9-]+\.)?(?:pixeldrain\.com/(?:u|l)/|gofile\.io/d/|[a-z0-9-]*fileditch[a-z0-9-]*\.[a-z]+/|bzzhr\.(?:to|co)/|buzzheavier\.com/|fuckingfast\.co/)[^\s'<>\)\u0022]+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            string best = null;
            int bestScore = -1;
            foreach (Match mm in m)
            {
                string raw = mm.Value;
                string url = DownloadHostPolicy.NormalizeDirectUrl(raw);
                int score = DownloadHostPolicy.SpeedScore(url);

                if (preferUpdate && System.Text.RegularExpressions.Regex.IsMatch(raw, @"(?i)update|patch|elamigos-update|tenoke-upd"))
                    score += 50;   // yama paketi tam repack'ten çok daha küçük

                if (score > bestScore)
                {
                    bestScore = score;
                    best = url;
                }
            }
            return best;
        }

        /// <summary>Başlıktan sürüm numarası çıkarır: "v1.2.3", "Build 23855724", "1.8.6".</summary>
        public static string ExtractVersion(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;

            Match build = Regex.Match(title, @"(?:build|b)\s*(\d{5,})", RegexOptions.IgnoreCase);
            if (build.Success) return build.Groups[1].Value;

            Match mv = Regex.Match(title, @"\bv?(\d+\.\d+(?:\.\d+){0,2})\b", RegexOptions.IgnoreCase);
            if (mv.Success) return mv.Groups[1].Value;

            return null;
        }

        /// <summary>Sürüm karşılaştırma: nokta ile ayrılmış sayısal parçalar + büyük build numaraları.</summary>
        public static int CompareVersions(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 0;
            if (string.IsNullOrEmpty(a)) return -1;
            if (string.IsNullOrEmpty(b)) return 1;

            string[] pa = a.Split('.');
            string[] pb = b.Split('.');
            int len = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < len; i++)
            {
                long na = 0, nb = 0;
                if (i < pa.Length) long.TryParse(Regex.Replace(pa[i], @"[^0-9]", ""), out na);
                if (i < pb.Length) long.TryParse(Regex.Replace(pb[i], @"[^0-9]", ""), out nb);
                if (na != nb) return na > nb ? 1 : -1;
            }
            return 0;
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]", "");
        }
    }
}
