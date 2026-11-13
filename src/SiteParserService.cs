using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace TitanGameHub
{
    public class SiteMirror
    {
        public string HostName { get; set; }
        public string Url { get; set; }
        public bool IsDirectArchive { get; set; }
        public int Priority { get; set; }
        /// <summary>Link "Update/Patch only" paketi mi (tam repack yerine sadece güncelleme).</summary>
        public bool IsUpdateOnlyPackage { get; set; }
    }

    public class SitePageAnalysis
    {
        public bool Success { get; set; }
        public string ResolvedPostUrl { get; set; }
        public string LiveVersion { get; set; }
        public string LiveFileSize { get; set; }
        public string ReleasedBy { get; set; }
        public string CleanDescription { get; set; }
        public string SystemRequirements { get; set; }
        public string CoverImageUrl { get; set; }
        public List<SiteMirror> Mirrors { get; set; }
        public string ErrorMessage { get; set; }

        public SitePageAnalysis()
        {
            Mirrors = new List<SiteMirror>();
        }
    }

    public class SiteCategoryDef
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string TagName { get; set; }

        public SiteCategoryDef(int id, string title, string tag)
        {
            Id = id;
            Title = title;
            TagName = tag;
        }
    }

    public static class SiteParserService
    {
        // Negative IDs below -10 represent Trusted Partner Sites:
        // -1  = Öne Çıkan Co-Op
        //  0  = SteamRIP Son Eklenenler
        // -10 = FitGirl.net (Pre-Installed + All DLCs)
        // -11 = FitGirl Repacks (Ultra Sıkıştırılmış + Magnet)
        // -12 = OvaGames (MediaFire / GoFile / PixelDrain)
        // -13 = FreeGOG PC Games (DRM-Free GOG)
        public static readonly SiteCategoryDef[] Categories = new SiteCategoryDef[]
        {
            new SiteCategoryDef(-20, "🌐 TÜM SİTELER (BİRLEŞİK CANLI)", "Tüm Siteler"),
            new SiteCategoryDef(-1, "★ TÜM OYUNLAR (EN İYİLER)", "Öne Çıkan"),
            new SiteCategoryDef(0, "🔥 SON EKLENENLER (CANLI)", "Son Eklenen"),
            new SiteCategoryDef(1538, "CO-OP & MULTIPLAYER", "Multiplayer"),
            new SiteCategoryDef(26, "AKSİYON (2700+)", "Aksiyon"),
            new SiteCategoryDef(32, "KORKU (520+)", "Korku"),
            new SiteCategoryDef(45, "AÇIK DÜNYA (300+)", "Açık Dünya"),
            new SiteCategoryDef(119, "HAYATTA KALMA", "Hayatta Kalma"),
            new SiteCategoryDef(37, "SİMÜLASYON (1600+)", "Simülasyon"),
            new SiteCategoryDef(41, "FPS / SHOOTER", "FPS & Shooter"),
            new SiteCategoryDef(27, "MACERA (2500+)", "Macera"),
            new SiteCategoryDef(63, "RPG (1200+)", "RPG"),
            new SiteCategoryDef(42, "STRATEJİ (1100+)", "Strateji"),
            new SiteCategoryDef(35, "YARIŞ (230+)", "Yarış")
        };

        private static readonly Dictionary<string, string> SteamAppIdCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private class CachedCatalog
        {
            public DateTime Timestamp;
            public List<GameProfile> Games;
            public string Status;
        }

        private static readonly Dictionary<string, CachedCatalog> CatalogCache = new Dictionary<string, CachedCatalog>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Tuple<DateTime, SitePageAnalysis>> AnalysisCache = new Dictionary<string, Tuple<DateTime, SitePageAnalysis>>(StringComparer.OrdinalIgnoreCase);

        static SiteParserService()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288 | (SecurityProtocolType)768;
            ServicePointManager.DefaultConnectionLimit = 64;
            ServicePointManager.Expect100Continue = false;
        }

        // =========================================================================
        // 1. MULTI-SITE LIVE CATALOG FETCHER (STEAMRIP + FITGIRL + OVAGAMES + FREEGOG)
        // =========================================================================
        /// <summary>
        /// Bağlı TÜM siteleri paralel çeker, tek listede birleştirir.
        /// Kullanıcı "TÜM SİTELER" seçtiğinde mağaza gerçek bir Steam vitrini gibi davranır.
        /// </summary>
        public static List<GameProfile> FetchAllSourcesMerged(string searchQuery, int page, int perPage, out string statusMessage)
        {
            statusMessage = "";
            int[] sources = new int[] { 0, -11, -12, -13, -14 };
            string[] names = new string[] { "SteamRIP", "FitGirl", "OvaGames", "FreeGOG", "CroTorrents" };

            List<GameProfile>[] buckets = new List<GameProfile>[sources.Length];
            string[] statuses = new string[sources.Length];
            ManualResetEvent[] done = new ManualResetEvent[sources.Length];

            for (int i = 0; i < sources.Length; i++)
            {
                int idx = i;
                done[i] = new ManualResetEvent(false);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        string st;
                        buckets[idx] = FetchWpJsonCatalogFromBaseUrl(GetBaseUrlForSource(sources[idx]), names[idx], 0, searchQuery, page, perPage, out st);
                        statuses[idx] = st;
                    }
                    catch { buckets[idx] = null; statuses[idx] = names[idx] + " yanıt vermedi"; }
                    finally { done[idx].Set(); }
                });
            }

            try { ManualResetEvent.WaitAll(done, 18000); }
            catch { }

            List<GameProfile> merged = new List<GameProfile>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int okSources = 0;
            for (int i = 0; i < sources.Length; i++)
            {
                if (buckets[i] == null || buckets[i].Count == 0) continue;
                okSources++;
                foreach (GameProfile gp in buckets[i])
                {
                    string key = (gp.Name ?? "").Trim().ToLowerInvariant();
                    if (key.Length == 0 || !seen.Add(key)) continue;
                    if (string.IsNullOrEmpty(gp.Category)) gp.Category = names[i];
                    merged.Add(gp);
                }
            }

            statusMessage = string.Format("🌐 TÜM SİTELER: {0}/{1} kaynak yanıt verdi • {2} oyun listelendi",
                okSources, sources.Length, merged.Count);
            return merged;
        }

        private static string GetBaseUrlForSource(int categoryId)
        {
            if (categoryId == -10 || categoryId == -11) return "https://fitgirl-repacks.site";
            if (categoryId == -12) return "https://www.ovagames.com";
            if (categoryId == -13) return "https://freegogpcgames.com";
            if (categoryId == -14) return "https://crotorrents.com";
            return "https://steamrip.com";
        }

        public static List<GameProfile> FetchLiveSiteGames(int categoryId, string searchQuery, int page, int perPage, out string statusMessage)
        {
            statusMessage = "";
            if (page < 1) page = 1;
            if (perPage < 6 || perPage > 50) perPage = 24;

            string cacheKey = string.Format("{0}_{1}_{2}_{3}", categoryId, searchQuery ?? "", page, perPage);
            lock (CatalogCache)
            {
                CachedCatalog cached;
                if (CatalogCache.TryGetValue(cacheKey, out cached))
                {
                    if ((DateTime.Now - cached.Timestamp).TotalMinutes < 5)
                    {
                        statusMessage = cached.Status;
                        return new List<GameProfile>(cached.Games);
                    }
                    CatalogCache.Remove(cacheKey);
                }
            }

            // Kendi katalog API'si yapılandırıldıysa önce onu dene (hızlı + imzalı kaynak).
            if (categoryId == 0 && CatalogApiClient.IsConfigured)
            {
                string apiStatus;
                List<GameProfile> apiGames = CatalogApiClient.Fetch(page, perPage, out apiStatus);
                if (apiGames != null && apiGames.Count > 0)
                {
                    statusMessage = apiStatus;
                    lock (CatalogCache)
                        CatalogCache[cacheKey] = new CachedCatalog { Games = new List<GameProfile>(apiGames), Timestamp = DateTime.Now, Status = apiStatus };
                    return apiGames;
                }
            }

            List<GameProfile> results = null;

            // TÜM SİTELER: bağlı tüm kaynakları paralel çekip birleştirir (SteamRIP + FitGirl + FitGirl + OvaGames + FreeGOG)
            if (categoryId == -20)
            {
                results = FetchAllSourcesMerged(searchQuery, page, perPage, out statusMessage);
                lock (CatalogCache)
                {
                    CatalogCache[cacheKey] = new CachedCatalog { Games = new List<GameProfile>(results), Timestamp = DateTime.Now, Status = statusMessage };
                }
                return results;
            }

            // Route to trusted partner sites if selected
            if (categoryId == -10)
            {
                results = FetchWpJsonCatalogFromBaseUrl("https://fitgirl-repacks.site", "FitGirl", 0, searchQuery, page, perPage, out statusMessage);
            }
            else if (categoryId == -11)
            {
                results = FetchWpJsonCatalogFromBaseUrl("https://fitgirl-repacks.site", "FitGirl", 0, searchQuery, page, perPage, out statusMessage);
            }
            else if (categoryId == -12)
            {
                results = FetchWpJsonCatalogFromBaseUrl("https://www.ovagames.com", "OvaGames", 0, searchQuery, page, perPage, out statusMessage);
            }
            else if (categoryId == -13)
            {
                results = FetchWpJsonCatalogFromBaseUrl("https://freegogpcgames.com", "FreeGOG", 0, searchQuery, page, perPage, out statusMessage);
            }
            else if (categoryId == -14)
            {
                results = FetchWpJsonCatalogFromBaseUrl("https://crotorrents.com", "CroTorrents", 0, searchQuery, page, perPage, out statusMessage);
            }
            else
            {
                // Try SteamRIP WP-JSON first; if Cloudflare challenges WP-JSON, combine FitGirl WP-JSON + SteamRIP Jina Reader!
                results = FetchWpJsonCatalogFromBaseUrl("https://steamrip.com", "SteamRIP", categoryId, searchQuery, page, perPage, out statusMessage);
                if (results == null || results.Count == 0)
                {
                    // Fallback: Query FitGirl WP-JSON (ultra-fast, zero Cloudflare blocks) + SteamRIP Jina Reader
                    string ggStatus;
                    List<GameProfile> ggGames = FetchWpJsonCatalogFromBaseUrl("https://fitgirl-repacks.site", "FitGirl", 0, searchQuery, page, perPage, out ggStatus);
                    if (ggGames != null && ggGames.Count > 0)
                    {
                        statusMessage = ggStatus;
                        results = ggGames;
                    }
                    else
                    {
                        results = FetchFallbackSearchViaJina(searchQuery, out statusMessage);
                    }
                }
            }

            if (results != null)
            {
                results = GameDeduplicationEngine.Deduplicate(results);
            }

            if (results != null && results.Count > 0)
            {
                lock (CatalogCache)
                {
                    CatalogCache[cacheKey] = new CachedCatalog
                    {
                        Timestamp = DateTime.Now,
                        Games = new List<GameProfile>(results),
                        Status = statusMessage
                    };
                }
            }

            return results ?? new List<GameProfile>();
        }

        public static void InvalidateCache()
        {
            lock (CatalogCache)
            {
                CatalogCache.Clear();
            }
            lock (AnalysisCache)
            {
                AnalysisCache.Clear();
            }
        }

        public static void InvalidateAnalysisCacheFor(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (AnalysisCache)
            {
                AnalysisCache.Remove(key);
            }
        }

        private static List<GameProfile> FetchWpJsonCatalogFromBaseUrl(
            string baseSiteUrl,
            string sourceLabel,
            int categoryId,
            string searchQuery,
            int page,
            int perPage,
            out string statusMessage)
        {
            statusMessage = "";
            List<GameProfile> results = new List<GameProfile>();

            try
            {
                StringBuilder apiUrl = new StringBuilder(baseSiteUrl.TrimEnd('/') + "/wp-json/wp/v2/posts?");
                apiUrl.Append("per_page=").Append(perPage);
                apiUrl.Append("&page=").Append(page);
                apiUrl.Append("&_fields=id,slug,link,title,content,categories,yoast_head_json");

                if (!string.IsNullOrWhiteSpace(searchQuery))
                {
                    apiUrl.Append("&search=").Append(Uri.EscapeDataString(searchQuery.Trim()));
                }
                else if (categoryId > 0)
                {
                    apiUrl.Append("&categories=").Append(categoryId);
                }

                string jsonRaw = DownloadStringWithHeaders(apiUrl.ToString());
                if (string.IsNullOrWhiteSpace(jsonRaw) || !jsonRaw.TrimStart().StartsWith("["))
                {
                    statusMessage = sourceLabel + " API yanıt vermedi.";
                    return results;
                }

                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                object[] posts = ser.Deserialize<object[]>(jsonRaw);
                if (posts == null || posts.Length == 0)
                {
                    statusMessage = sourceLabel + " üzerinde sonuç bulunamadı.";
                    return results;
                }

                foreach (object postObj in posts)
                {
                    Dictionary<string, object> post = postObj as Dictionary<string, object>;
                    if (post == null) continue;

                    GameProfile gp = ParseWpPostObjectToGameProfile(post, categoryId, sourceLabel);
                    if (gp != null && !string.IsNullOrWhiteSpace(gp.Name))
                    {
                        if (gp.Name.IndexOf("Upcoming Repacks", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        results.Add(gp);
                    }
                }

                statusMessage = string.Format("✔ {0} Canlı Veritabanı: {1} oyun yüklendi (Sayfa {2})", sourceLabel, results.Count, page);
                return results;
            }
            catch (Exception ex)
            {
                statusMessage = sourceLabel + " bağlantı uyarısı: " + ex.Message;
                return results;
            }
        }

        private static GameProfile ParseWpPostObjectToGameProfile(Dictionary<string, object> post, int requestedCatId, string sourceLabel)
        {
            try
            {
                string slug = post.ContainsKey("slug") ? Convert.ToString(post["slug"]) : "";
                string link = post.ContainsKey("link") ? Convert.ToString(post["link"]) : "";

                string rawTitle = "";
                if (post.ContainsKey("title") && post["title"] is Dictionary<string, object>)
                {
                    Dictionary<string, object> titleDict = (Dictionary<string, object>)post["title"];
                    if (titleDict.ContainsKey("rendered"))
                        rawTitle = Convert.ToString(titleDict["rendered"]);
                }

                string cleanName = CleanGameTitle(rawTitle);
                if (string.IsNullOrWhiteSpace(cleanName)) return null;

                string contentHtml = "";
                if (post.ContainsKey("content") && post["content"] is Dictionary<string, object>)
                {
                    Dictionary<string, object> contentDict = (Dictionary<string, object>)post["content"];
                    if (contentDict.ContainsKey("rendered"))
                        contentHtml = Convert.ToString(contentDict["rendered"]);
                }

                string ogImage = "";
                if (post.ContainsKey("yoast_head_json") && post["yoast_head_json"] is Dictionary<string, object>)
                {
                    Dictionary<string, object> yoast = (Dictionary<string, object>)post["yoast_head_json"];
                    if (yoast.ContainsKey("og_image") && yoast["og_image"] is object[])
                    {
                        object[] imgs = (object[])yoast["og_image"];
                        if (imgs.Length > 0 && imgs[0] is Dictionary<string, object>)
                        {
                            Dictionary<string, object> firstImg = (Dictionary<string, object>)imgs[0];
                            if (firstImg.ContainsKey("url"))
                                ogImage = Convert.ToString(firstImg["url"]);
                        }
                    }
                }

                if (string.IsNullOrEmpty(ogImage) && !string.IsNullOrEmpty(contentHtml))
                {
                    Match imgMatch = Regex.Match(contentHtml, @"https?://[^""'\s<>]+\.(?:jpg|jpeg|png|webp)", RegexOptions.IgnoreCase);
                    if (imgMatch.Success) ogImage = imgMatch.Value;
                }

                SitePageAnalysis details = ParseHtmlContentDetails(contentHtml, link);

                // Extract version from title if present (e.g. [v1.2.1+ALL DLCs])
                if (string.IsNullOrEmpty(details.LiveVersion))
                {
                    Match titleVer = Regex.Match(rawTitle, @"\[v?([0-9]+\.[0-9\.]+[^\]]*)\]", RegexOptions.IgnoreCase);
                    if (titleVer.Success) details.LiveVersion = "v" + titleVer.Groups[1].Value.Trim();
                }

                string categoryTag = ResolveCategoryTag(post, requestedCatId);
                if (!string.IsNullOrEmpty(sourceLabel) && sourceLabel != "SteamRIP")
                    categoryTag = sourceLabel + " • " + categoryTag;

                string primaryMirror = (details.Mirrors.Count > 0) ? details.Mirrors[0].Url : "";
                string versionStr = !string.IsNullOrEmpty(details.LiveVersion) ? details.LiveVersion : "Son Sürüm";
                string sizeStr = !string.IsNullOrEmpty(details.LiveFileSize) ? details.LiveFileSize : "Hazır Paket";
                string descStr = !string.IsNullOrEmpty(details.CleanDescription)
                    ? details.CleanDescription
                    : (cleanName + " — " + sourceLabel + " Pre-Installed hazır oyun paketi. Tek tıkla doğrudan uygulama içinden indirilebilir ve Spacewar (AppID 480) ile oynanabilir.");

                bool isMultiplayer = (requestedCatId == 1538) ||
                                     contentHtml.IndexOf("Multiplayer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     contentHtml.IndexOf("Co-op", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     contentHtml.IndexOf("Online", StringComparison.OrdinalIgnoreCase) >= 0;

                GameProfile gp = new GameProfile
                {
                    Id = !string.IsNullOrEmpty(slug) ? slug : Guid.NewGuid().ToString("N").Substring(0, 8),
                    Name = cleanName,
                    Category = categoryTag,
                    Version = versionStr,
                    RemoteVersion = versionStr,
                    FileSize = sizeStr,
                    MaxPlayers = isMultiplayer ? "Online Co-Op & Multiplayer" : "Tek & Çok Oyunculu",
                    SteamAppId = "480",
                    BannerUrl = ogImage,
                    CapsuleUrl = ogImage,
                    DownloadUrl = link,
                    DirectMirrorUrl = primaryMirror,
                    Description = descStr,
                    ExecutablePath = "",
                    WorkingDirectory = "",
                    LastPlayedText = "Henüz Oynanmadı",
                    IsFavorite = false,
                    PlayTimeMinutes = 0
                };

                return gp;
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveCategoryTag(Dictionary<string, object> post, int requestedCatId)
        {
            if (requestedCatId > 0)
            {
                foreach (SiteCategoryDef def in Categories)
                {
                    if (def.Id == requestedCatId) return def.TagName;
                }
            }

            if (post.ContainsKey("categories") && post["categories"] is object[])
            {
                object[] cats = (object[])post["categories"];
                foreach (object c in cats)
                {
                    int cid = Convert.ToInt32(c);
                    if (cid == 1538) return "Multiplayer & Co-Op";
                }
                foreach (object c in cats)
                {
                    int cid = Convert.ToInt32(c);
                    foreach (SiteCategoryDef def in Categories)
                    {
                        if (def.Id == cid && cid > 0) return def.TagName;
                    }
                }
            }
            return "Pre-Installed";
        }

        public static string CleanGameTitle(string rawTitle)
        {
            if (string.IsNullOrWhiteSpace(rawTitle)) return "";
            string decoded = WebUtility.HtmlDecode(rawTitle);
            decoded = Regex.Replace(decoded, @"\s*Free\s+Download.*$", "", RegexOptions.IgnoreCase);
            decoded = Regex.Replace(decoded, @"\s*\(v[0-9\.]+.*?\)\s*$", "", RegexOptions.IgnoreCase);
            decoded = Regex.Replace(decoded, @"\s*\[v?[0-9\.]+.*?\]\s*$", "", RegexOptions.IgnoreCase);
            return decoded.Trim();
        }

        // =========================================================================
        // 2. MULTI-SITE DEEP MIRROR AGGREGATOR (STEAMRIP + FITGIRL + JINA)
        // =========================================================================
        public static SitePageAnalysis AnalyzeGameOnSteamRip(GameProfile gp)
        {
            SitePageAnalysis result = new SitePageAnalysis();
            if (gp == null)
            {
                result.ErrorMessage = "Profil seçilmedi.";
                return result;
            }

            string analysisKey = gp.Id ?? gp.Name ?? "";
            lock (AnalysisCache)
            {
                Tuple<DateTime, SitePageAnalysis> cached;
                if (AnalysisCache.TryGetValue(analysisKey, out cached))
                {
                    if ((DateTime.Now - cached.Item1).TotalMinutes < 15 && cached.Item2 != null && cached.Item2.Mirrors.Count > 0)
                    {
                        ApplyAnalysisToProfile(gp, cached.Item2);
                        return cached.Item2;
                    }
                    AnalysisCache.Remove(analysisKey);
                }
            }

            HashSet<string> seenMirrorUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;

                // Source 1: Try SteamRIP WP-JSON if accessible
                string slug = ExtractSlugFromUrl(gp.DownloadUrl);
                string wpJsonUrl = !string.IsNullOrEmpty(slug)
                    ? ("https://steamrip.com/wp-json/wp/v2/posts?slug=" + Uri.EscapeDataString(slug) + "&_fields=id,slug,link,title,content,yoast_head_json")
                    : ("https://steamrip.com/wp-json/wp/v2/posts?search=" + Uri.EscapeDataString(gp.Name) + "&per_page=5&_fields=id,slug,link,title,content,yoast_head_json");

                string json = DownloadStringWithHeaders(wpJsonUrl);
                if (!string.IsNullOrWhiteSpace(json) && json.TrimStart().StartsWith("["))
                {
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    ser.MaxJsonLength = int.MaxValue;
                    object[] arr = ser.Deserialize<object[]>(json);
                    if (arr != null && arr.Length > 0)
                    {
                        Dictionary<string, object> bestPost = SelectBestMatchingPost(arr, gp.Name);
                        if (bestPost != null && bestPost.ContainsKey("content") && bestPost["content"] is Dictionary<string, object>)
                        {
                            string link = bestPost.ContainsKey("link") ? Convert.ToString(bestPost["link"]) : gp.DownloadUrl;
                            Dictionary<string, object> cDict = (Dictionary<string, object>)bestPost["content"];
                            string html = cDict.ContainsKey("rendered") ? Convert.ToString(cDict["rendered"]) : "";
                            SitePageAnalysis srRes = ParseHtmlContentDetails(html, link);
                            MergeAnalysisResults(result, srRes, seenMirrorUrls);
                        }
                    }
                }

                // Source 2: Jina Reader on SteamRIP (Bypasses Cloudflare 100% of the time!)
                string targetPostUrl = gp.DownloadUrl;
                if (string.IsNullOrWhiteSpace(targetPostUrl) || !targetPostUrl.StartsWith("http"))
                {
                    targetPostUrl = FindSteamRipPostUrl(gp.Name);
                }
                if (!string.IsNullOrWhiteSpace(targetPostUrl) && targetPostUrl.IndexOf("steamrip.com", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string markdown = FetchViaJinaReader(targetPostUrl);
                    if (!string.IsNullOrWhiteSpace(markdown))
                    {
                        if (markdown.IndexOf("404: Not Found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            markdown.IndexOf("Page not found", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // Target URL is dead or slug was changed on SteamRIP! Self-heal via fresh live search
                            string freshUrl = FindSteamRipPostUrl(gp.Name);
                            if (!string.IsNullOrEmpty(freshUrl) && !freshUrl.Equals(targetPostUrl, StringComparison.OrdinalIgnoreCase))
                            {
                                targetPostUrl = freshUrl;
                                gp.DownloadUrl = freshUrl;
                                markdown = FetchViaJinaReader(targetPostUrl);
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(markdown))
                        {
                            SitePageAnalysis jinaRes = ParseMarkdownContentDetails(markdown, targetPostUrl);
                            if (jinaRes.Mirrors.Count == 0 && !string.IsNullOrWhiteSpace(gp.Name))
                            {
                                // Outdated slug without 404? Try searching title
                                string freshUrl = FindSteamRipPostUrl(gp.Name);
                                if (!string.IsNullOrEmpty(freshUrl) && !freshUrl.Equals(targetPostUrl, StringComparison.OrdinalIgnoreCase))
                                {
                                    string freshMd = FetchViaJinaReader(freshUrl);
                                    if (!string.IsNullOrWhiteSpace(freshMd))
                                    {
                                        SitePageAnalysis freshRes = ParseMarkdownContentDetails(freshMd, freshUrl);
                                        if (freshRes.Mirrors.Count > 0)
                                        {
                                            jinaRes = freshRes;
                                            targetPostUrl = freshUrl;
                                            gp.DownloadUrl = freshUrl;
                                        }
                                    }
                                }
                            }
                            MergeAnalysisResults(result, jinaRes, seenMirrorUrls);
                        }
                    }
                }

                // Source 4: SkidrowReloaded Multi-Host Search via Jina Reader (Finds PixelDrain, MediaFire, GoFile)
                if (!string.IsNullOrWhiteSpace(gp.Name) && (result.Mirrors.Count == 0 || !result.Mirrors.Exists(delegate(SiteMirror m) { return m.IsDirectArchive; })))
                {
                    try
                    {
                        string srSearch = "https://www.skidrowreloaded.com/?s=" + Uri.EscapeDataString(gp.Name.Trim());
                        string srMd = FetchViaJinaReader(srSearch);
                        if (!string.IsNullOrEmpty(srMd))
                        {
                            string cleanKey = Regex.Replace(gp.Name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
                            MatchCollection postMatches = Regex.Matches(srMd, @"https://www\.skidrowreloaded\.com/([a-z0-9-]+-v[0-9][a-z0-9-]*/?)", RegexOptions.IgnoreCase);
                            if (postMatches.Count == 0)
                                postMatches = Regex.Matches(srMd, @"https://www\.skidrowreloaded\.com/([a-z0-9-]+)/", RegexOptions.IgnoreCase);

                            string bestSrUrl = null;
                            foreach (Match pm in postMatches)
                            {
                                string pUrl = pm.Value;
                                if (pUrl.IndexOf("/category/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    pUrl.IndexOf("/page/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    pUrl.IndexOf("/games-list/", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                                if (pUrl.ToLowerInvariant().Contains(cleanKey))
                                {
                                    bestSrUrl = pUrl;
                                    break;
                                }
                                if (bestSrUrl == null) bestSrUrl = pUrl;
                            }

                            if (!string.IsNullOrEmpty(bestSrUrl))
                            {
                                string postMd = FetchViaJinaReader(bestSrUrl);
                                if (!string.IsNullOrEmpty(postMd))
                                {
                                    SitePageAnalysis srAnalysis = ParseMarkdownContentDetails(postMd, bestSrUrl);
                                    MergeAnalysisResults(result, srAnalysis, seenMirrorUrls);
                                }
                            }
                        }
                    }
                    catch { }
                }

                // Source 5: Online-Fix Torrent / P2P Swarm
                if (!string.IsNullOrWhiteSpace(gp.Name) && (result.Mirrors.Count == 0 || !result.Mirrors.Exists(delegate(SiteMirror m) { return m.IsDirectArchive; })))
                {
                    try
                    {
                        string ofUrl = "https://uploads.online-fix.me:2053/torrents/" + Uri.EscapeDataString(gp.Name) + "/";
                        string ofMd = DownloadStringWithHeaders(ofUrl);
                        if (!string.IsNullOrEmpty(ofMd))
                        {
                            Match tMatch = Regex.Match(ofMd, @"href=[""']([^""']+\.torrent)[""']", RegexOptions.IgnoreCase);
                            if (tMatch.Success)
                            {
                                string torrentFull = ofUrl.TrimEnd('/') + "/" + tMatch.Groups[1].Value.TrimStart('/');
                                if (!seenMirrorUrls.Contains(torrentFull))
                                {
                                    seenMirrorUrls.Add(torrentFull);
                                    result.Mirrors.Add(new SiteMirror
                                    {
                                        HostName = "Online-Fix P2P Ağı (.torrent)",
                                        Url = torrentFull,
                                        IsDirectArchive = true,
                                        Priority = 7
                                    });
                                }
                            }
                        }
                    }
                    catch { }
                }

                // Sort all discovered mirrors across all sites so verified Direct Binary streams come first!
                result.Mirrors.Sort(delegate(SiteMirror a, SiteMirror b)
                {
                    if (a.IsDirectArchive != b.IsDirectArchive)
                        return b.IsDirectArchive.CompareTo(a.IsDirectArchive);
                    return a.Priority.CompareTo(b.Priority);
                });
                result.Success = result.Mirrors.Count > 0 || !string.IsNullOrEmpty(result.LiveVersion);

                ApplyAnalysisToProfile(gp, result);

                if (result.Mirrors.Count > 0 || !string.IsNullOrEmpty(result.LiveVersion))
                {
                    lock (AnalysisCache)
                    {
                        AnalysisCache[analysisKey] = new Tuple<DateTime, SitePageAnalysis>(DateTime.Now, result);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private static void MergeAnalysisResults(SitePageAnalysis target, SitePageAnalysis source, HashSet<string> seenUrls)
        {
            if (source == null) return;
            if (string.IsNullOrEmpty(target.ResolvedPostUrl) && !string.IsNullOrEmpty(source.ResolvedPostUrl))
                target.ResolvedPostUrl = source.ResolvedPostUrl;
            if (string.IsNullOrEmpty(target.LiveVersion) && !string.IsNullOrEmpty(source.LiveVersion))
                target.LiveVersion = source.LiveVersion;
            if (string.IsNullOrEmpty(target.LiveFileSize) && !string.IsNullOrEmpty(source.LiveFileSize))
                target.LiveFileSize = source.LiveFileSize;
            if (string.IsNullOrEmpty(target.ReleasedBy) && !string.IsNullOrEmpty(source.ReleasedBy))
                target.ReleasedBy = source.ReleasedBy;
            if (string.IsNullOrEmpty(target.CleanDescription) && !string.IsNullOrEmpty(source.CleanDescription))
                target.CleanDescription = source.CleanDescription;

            foreach (SiteMirror m in source.Mirrors)
            {
                if (!seenUrls.Contains(m.Url))
                {
                    seenUrls.Add(m.Url);
                    target.Mirrors.Add(m);
                }
            }
        }

        private static Dictionary<string, object> SelectBestMatchingPost(object[] posts, string targetName)
        {
            if (posts == null || posts.Length == 0) return null;
            Dictionary<string, object> strict = SelectStrictMatchingPost(posts, targetName);
            if (strict != null) return strict;
            return posts[0] as Dictionary<string, object>;
        }

        private static Dictionary<string, object> SelectStrictMatchingPost(object[] posts, string targetName)
        {
            if (posts == null || posts.Length == 0) return null;
            string cleanTarget = Regex.Replace((targetName ?? "").ToLowerInvariant(), @"[^a-z0-9]", "");
            if (string.IsNullOrEmpty(cleanTarget)) return null;

            foreach (object obj in posts)
            {
                Dictionary<string, object> p = obj as Dictionary<string, object>;
                if (p == null) continue;
                if (p.ContainsKey("title") && p["title"] is Dictionary<string, object>)
                {
                    Dictionary<string, object> tDict = (Dictionary<string, object>)p["title"];
                    string t = CleanGameTitle(Convert.ToString(tDict["rendered"]));
                    string cleanPost = Regex.Replace(t.ToLowerInvariant(), @"[^a-z0-9]", "");
                    if (cleanPost == cleanTarget ||
                        (cleanTarget.Length >= 5 && (cleanPost.StartsWith(cleanTarget) || cleanTarget.StartsWith(cleanPost))))
                    {
                        return p;
                    }
                }
            }
            return null;
        }

        private static void ApplyAnalysisToProfile(GameProfile gp, SitePageAnalysis result)
        {
            if (gp == null || result == null) return;
            if (!string.IsNullOrEmpty(result.ResolvedPostUrl) && string.IsNullOrEmpty(gp.DownloadUrl))
                gp.DownloadUrl = result.ResolvedPostUrl;

            if (!string.IsNullOrEmpty(result.LiveVersion))
            {
                gp.RemoteVersion = result.LiveVersion;
                if (!gp.IsInstalled) gp.Version = result.LiveVersion;
            }
            if (!string.IsNullOrEmpty(result.LiveFileSize))
                gp.FileSize = result.LiveFileSize;

            if (!string.IsNullOrEmpty(result.CleanDescription) && (string.IsNullOrEmpty(gp.Description) || gp.Description.Contains("Pre-Installed")))
                gp.Description = result.CleanDescription;

            if (result.Mirrors.Count > 0)
            {
                SiteMirror direct = result.Mirrors.Find(delegate(SiteMirror m) { return m.IsDirectArchive; });
                if (direct != null)
                    gp.DirectMirrorUrl = direct.Url;
                else if (string.IsNullOrEmpty(gp.DirectMirrorUrl))
                    gp.DirectMirrorUrl = result.Mirrors[0].Url;
            }
        }

        private static SitePageAnalysis ParseHtmlContentDetails(string html, string postUrl)
        {
            SitePageAnalysis res = new SitePageAnalysis { ResolvedPostUrl = postUrl };
            if (string.IsNullOrEmpty(html)) return res;

            // 1. Parse Version
            Match verMatch = Regex.Match(html, @"<strong>\s*Version\s*[:]*\s*</strong>\s*[:]*\s*([^<\r\n]+)", RegexOptions.IgnoreCase);
            if (verMatch.Success)
                res.LiveVersion = CleanTextFragment(verMatch.Groups[1].Value);

            // 2. Parse Game Size
            Match sizeMatch = Regex.Match(html, @"(?:Game\s+Size|File\s+Size|Repack\s+Size)\s*[:<>/strong\s]*([0-9\.,]+\s*(?:GB|MB))", RegexOptions.IgnoreCase);
            if (sizeMatch.Success)
                res.LiveFileSize = CleanTextFragment(sizeMatch.Groups[1].Value);

            // 3. Parse Released By
            Match relMatch = Regex.Match(html, @"<strong>\s*Released\s+By\s*[:]*\s*</strong>\s*[:]*\s*([^<\r\n]+)", RegexOptions.IgnoreCase);
            if (relMatch.Success)
                res.ReleasedBy = CleanTextFragment(relMatch.Groups[1].Value);

            // 4. Parse Clean Description
            MatchCollection pMatches = Regex.Matches(html, @"<p[^>]*>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            StringBuilder descSb = new StringBuilder();
            foreach (Match pm in pMatches)
            {
                string inner = StripHtmlTags(pm.Groups[1].Value).Trim();
                inner = Regex.Replace(inner, @"\[/?vc_[^\]]+\]", "").Trim();
                if (inner.Length < 35) continue;
                if (inner.IndexOf("DOWNLOAD HERE", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (inner.IndexOf("How to Run", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                descSb.Append(inner).Append(" ");
                if (descSb.Length > 340) break;
            }
            if (descSb.Length > 0)
                res.CleanDescription = descSb.ToString().Trim();

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 5A. Extract standard <a href="..."> download links (including protocol-relative //)
            MatchCollection hrefs = Regex.Matches(html, @"href=[""']((?:https?:)?//[^""'\s<>]+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (Match m in hrefs)
            {
                string url = WebUtility.HtmlDecode(m.Groups[1].Value.Trim()).Replace("\\/", "/");
                if (url.StartsWith("//")) url = "https:" + url;
                TryAddSupportedMirror(url, seen, res.Mirrors);
            }

            // 5B. Extract FitGirl Visual Composer [vc_btn ... link="url:https%3A%2F%2F..."] download links
            MatchCollection vcLinks = Regex.Matches(html, @"url:((?:https?:)?(?:%2F%2F|//)[^\|""'\s\\]+|https?%3A%2F%2F[^\|""'\s\\]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (Match vm in vcLinks)
            {
                try
                {
                    string decodedUrl = Uri.UnescapeDataString(vm.Groups[1].Value).Trim();
                    if (decodedUrl.StartsWith("//")) decodedUrl = "https:" + decodedUrl;
                    TryAddSupportedMirror(decodedUrl, seen, res.Mirrors);
                }
                catch { }
            }

            // 5C. Extract raw GoFile / PixelDrain / Fileditch / Buzzheavier URLs that might appear without an <a> tag
            MatchCollection rawLinks = Regex.Matches(html, @"((?:https?:)?//(?:www\.)?(?:gofile\.io/d/|pixeldrain\.com/(?:u|l)/|(?:[a-zA-Z0-9-]+\.)?fileditchfiles\.[a-z]+/|buzzheavier\.com/|bzzhr\.(?:co|to)/)[^\s\""'<>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (Match rm in rawLinks)
            {
                string url = rm.Groups[1].Value.Trim().Replace("\\/", "/");
                if (url.StartsWith("//")) url = "https:" + url;
                TryAddSupportedMirror(url, seen, res.Mirrors);
            }

            res.Mirrors.Sort(delegate(SiteMirror a, SiteMirror b) { return a.Priority.CompareTo(b.Priority); });
            res.Success = res.Mirrors.Count > 0 || !string.IsNullOrEmpty(res.LiveVersion);
            return res;
        }

        private static SitePageAnalysis ParseMarkdownContentDetails(string markdown, string postUrl)
        {
            SitePageAnalysis result = new SitePageAnalysis { ResolvedPostUrl = postUrl };

            Match verMatch = Regex.Match(markdown, @"\*\*Version\*\*[:\s]*([^\r\n\*]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (verMatch.Success)
                result.LiveVersion = verMatch.Groups[1].Value.Trim().TrimStart(':').Trim();

            Match sizeMatch = Regex.Match(markdown, @"\*\*Game Size[:\*]*\s*([^\r\n\*]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (sizeMatch.Success)
                result.LiveFileSize = sizeMatch.Groups[1].Value.Trim().TrimStart(':').Trim();

            Match relMatch = Regex.Match(markdown, @"\*\*Released By[:\*]*\s*([^\r\n\*]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (relMatch.Success)
                result.ReleasedBy = relMatch.Groups[1].Value.Trim().TrimStart(':').Trim();

            HashSet<string> seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Markdown link format: [Text](url)
            MatchCollection linkMatches = Regex.Matches(markdown, @"\(((?:https?:)?//[^\s\)\""]+)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (Match m in linkMatches)
            {
                string url = m.Groups[1].Value.Trim();
                if (url.StartsWith("//")) url = "https:" + url;
                TryAddSupportedMirror(url, seenUrls, result.Mirrors);
            }

            // 2. HTML href tags that might remain in Jina markdown output
            MatchCollection hrefMatches = Regex.Matches(markdown, @"href=[""']((?:https?:)?//[^""'\s<>]+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (Match hm in hrefMatches)
            {
                string url = WebUtility.HtmlDecode(hm.Groups[1].Value.Trim()).Replace("\\/", "/");
                if (url.StartsWith("//")) url = "https:" + url;
                TryAddSupportedMirror(url, seenUrls, result.Mirrors);
            }

            // 3. Raw GoFile / PixelDrain / Fileditch URLs in markdown body
            MatchCollection rawMatches = Regex.Matches(markdown, @"((?:https?:)?//(?:www\.)?(?:gofile\.io/d/|pixeldrain\.com/(?:u|l)/|(?:[a-zA-Z0-9-]+\.)?fileditchfiles\.[a-z]+/|buzzheavier\.com/|bzzhr\.(?:co|to)/)[^\s\)\""'<>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (Match rm in rawMatches)
            {
                string url = rm.Groups[1].Value.Trim();
                if (url.StartsWith("//")) url = "https:" + url;
                TryAddSupportedMirror(url, seenUrls, result.Mirrors);
            }

            result.Mirrors.Sort(delegate(SiteMirror a, SiteMirror b) { return a.Priority.CompareTo(b.Priority); });
            result.Success = result.Mirrors.Count > 0 || !string.IsNullOrEmpty(result.LiveVersion);
            return result;
        }

        private static void TryAddSupportedMirror(string url, HashSet<string> seen, List<SiteMirror> list)
        {
            if (string.IsNullOrEmpty(url)) return;

            // Unwrap any embedded redirect wrapper (?url=, ?link=, ?target=)
            Match inner = Regex.Match(url, @"[?&](?:url|link|target|to|file)=((?:https?%3A%2F%2F|https?://)[^\s&""']+)", RegexOptions.IgnoreCase);
            if (inner.Success)
            {
                string unescaped = Uri.UnescapeDataString(inner.Groups[1].Value);
                TryAddSupportedMirror(unescaped, seen, list);
            }

            if (seen.Contains(url)) return;
            string hostName;
            bool isDirect;
            int priority;
            if (IsSupportedMirrorUrl(url, out hostName, out isDirect, out priority))
            {
                seen.Add(url);
                list.Add(new SiteMirror
                {
                    HostName = hostName,
                    Url = url,
                    IsDirectArchive = isDirect,
                    Priority = priority
                });
            }
        }

        private static List<GameProfile> FetchFallbackSearchViaJina(string query, out string statusMsg)
        {
            List<GameProfile> list = new List<GameProfile>();
            statusMsg = "";
            try
            {
                string url = string.IsNullOrWhiteSpace(query)
                    ? "https://steamrip.com/"
                    : ("https://steamrip.com/?s=" + Uri.EscapeDataString(query.Trim()));
                string md = FetchViaJinaReader(url);
                if (string.IsNullOrEmpty(md))
                {
                    statusMsg = "Siteye erişilemedi.";
                    return list;
                }

                MatchCollection matches = Regex.Matches(md, @"\[([^\]]+)\s+Free\s+Download[^\]]*\]\((https://steamrip\.com/[a-z0-9-]+-free-download[a-z0-9-]*/?)\)", RegexOptions.IgnoreCase);
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Match m in matches)
                {
                    string title = CleanGameTitle(m.Groups[1].Value);
                    string link = m.Groups[2].Value;
                    if (string.IsNullOrEmpty(title) || seen.Contains(link)) continue;
                    seen.Add(link);

                    list.Add(new GameProfile
                    {
                        Id = ExtractSlugFromUrl(link) ?? Guid.NewGuid().ToString("N").Substring(0, 8),
                        Name = title,
                        Category = "SteamRIP Bulut",
                        Version = "Son Sürüm",
                        RemoteVersion = "Son Sürüm",
                        FileSize = "Bulut",
                        MaxPlayers = "Online & Tek Oyunculu",
                        SteamAppId = "480",
                        DownloadUrl = link,
                        Description = title + " — SteamRIP hazır oyun paketi."
                    });
                }

                statusMsg = string.Format("SteamRIP Jina Kanalı: {0} oyun bulundu.", list.Count);
            }
            catch (Exception ex)
            {
                statusMsg = "Yedek arama hatası: " + ex.Message;
            }
            return list;
        }

        // =========================================================================
        // 3. STEAM STORE OFFICIAL METADATA & HEADER IMAGE RESOLVER
        // =========================================================================
        public static void ResolveSteamStoreMetadataAsync(GameProfile gp, Action onResolved)
        {
            if (gp == null || string.IsNullOrWhiteSpace(gp.Name)) return;

            string cachedAppId;
            if (SteamAppIdCache.TryGetValue(gp.Name, out cachedAppId))
            {
                gp.SteamAppId = cachedAppId;
                if (string.IsNullOrEmpty(gp.BannerUrl))
                    gp.BannerUrl = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/" + cachedAppId + "/header.jpg";
                if (onResolved != null) onResolved();
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string cleanSearch = Regex.Replace(gp.Name, @"\([^)]*\)", "").Trim();
                    string apiUrl = "https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(cleanSearch) + "&l=turkish&cc=TR";
                    string json = DownloadStringWithHeaders(apiUrl);
                    if (string.IsNullOrEmpty(json)) return;

                    Match idMatch = Regex.Match(json, @"""id""\s*:\s*([0-9]+)");
                    if (idMatch.Success)
                    {
                        string appId = idMatch.Groups[1].Value;
                        SteamAppIdCache[gp.Name] = appId;
                        gp.SteamAppId = appId;
                        string steamHeader = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/" + appId + "/header.jpg";
                        if (string.IsNullOrEmpty(gp.BannerUrl))
                        {
                            gp.BannerUrl = steamHeader;
                            gp.CapsuleUrl = steamHeader;
                        }
                        else
                        {
                            gp.CapsuleUrl = steamHeader;
                        }
                        if (onResolved != null) onResolved();
                    }
                }
                catch { }
            });
        }

        public static string FindSteamRipPostUrl(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName)) return null;
            try
            {
                string searchUrl = "https://steamrip.com/?s=" + Uri.EscapeDataString(gameName.Trim());
                string md = FetchViaJinaReader(searchUrl);
                if (string.IsNullOrEmpty(md)) return null;

                string cleanKey = Regex.Replace(gameName.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
                MatchCollection postLinks = Regex.Matches(md, @"https://steamrip\.com/([a-z0-9-]+-free-download[a-z0-9-]*/?)", RegexOptions.IgnoreCase);

                string firstCandidate = null;
                foreach (Match m in postLinks)
                {
                    string fullUrl = m.Value;
                    string slug = m.Groups[1].Value.ToLowerInvariant();
                    if (firstCandidate == null) firstCandidate = fullUrl;

                    if (!string.IsNullOrEmpty(cleanKey) && slug.Contains(cleanKey))
                        return fullUrl;
                }
                return firstCandidate;
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractSlugFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            Match m = Regex.Match(url, @"steamrip\.com/([a-zA-Z0-9_-]+)/?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success) return m.Groups[1].Value;
            return null;
        }

        private static string DownloadStringWithHeaders(string url)
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
                req.Accept = "application/json, text/plain, */*";
                req.Timeout = 9000;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (System.IO.StreamReader sr = new System.IO.StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }

        private static string FetchViaJinaReader(string targetUrl)
        {
            return DownloadStringWithHeaders("https://r.jina.ai/" + targetUrl);
        }

        private static string StripHtmlTags(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            string noTags = Regex.Replace(input, @"<[^>]+>", " ");
            noTags = WebUtility.HtmlDecode(noTags);
            return Regex.Replace(noTags, @"\s+", " ").Trim();
        }

        private static string CleanTextFragment(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return StripHtmlTags(input).Trim().TrimStart(':').Trim();
        }

        private static bool IsSupportedMirrorUrl(string url, out string hostName, out bool isDirectArchive, out int priority)
        {
            hostName = "Mirror";
            isDirectArchive = false;
            priority = 50;
            if (string.IsNullOrEmpty(url)) return false;

            string u = url.ToLowerInvariant();
            if (u.Contains("steamrip.com") || u.Contains("discord.gg") ||
                u.Contains("reddit.com") || u.Contains("steamdb.info") || u.Contains("wp-content") ||
                u.Contains("steampowered.com") || u.Contains("youtu.be") || u.Contains("youtube.com"))
                return false;

            // Priority 1: PixelDrain -> 100% In-App Direct High-Speed API Stream (No Captchas)
            if (u.Contains("pixeldrain.com"))
            {
                hostName = "PixelDrain (Doğrudan API - 50MB/s)";
                isDirectArchive = true;
                priority = 1;
                return true;
            }
            // Priority 1: GoFile.io -> Resolved 100% in-app to direct binary stream via GoFile API + SHA256 WT token!
            if (u.Contains("gofile.io"))
            {
                hostName = "GoFile (Direkt API Akışı)";
                isDirectArchive = true;
                priority = 1;
                return true;
            }
            // Priority 2: MediaFire -> 100% In-App Direct Stream
            if (u.Contains("mediafire.com"))
            {
                hostName = "MediaFire (Doğrudan Akış)";
                isDirectArchive = true;
                priority = 2;
                return true;
            }
            // Priority 2: VikingFile Direct High Speed Stream
            if (u.Contains("vikingfile.com"))
            {
                hostName = "VikingFile (Yüksek Hız)";
                isDirectArchive = true;
                priority = 2;
                return true;
            }
            // Priority 2: Fileditch Direct Archive Stream
            if (u.Contains("fileditchfiles.") || u.Contains("itsfileditchfiles."))
            {
                hostName = "Fileditch (Doğrudan CDN)";
                isDirectArchive = true;
                priority = 2;
                return true;
            }
            // Priority 3: Buzzheavier / bzzhr.to / bzzhr.co
            if (u.Contains("buzzheavier.com") || u.Contains("bzzhr.to") || u.Contains("bzzhr.co"))
            {
                hostName = "Buzzheavier (Ultra Hız)";
                isDirectArchive = true;
                priority = 3;
                return true;
            }
            // Priority 4: FuckingFast.co (FitGirl 500MB/s mirror)
            if (u.Contains("fuckingfast.co"))
            {
                hostName = "FuckingFast (500MB/s)";
                isDirectArchive = true;
                priority = 4;
                return true;
            }
            // Priority 5: Qiwi.gg / SpyderRock
            if (u.Contains("qiwi.gg") || u.Contains("spyderrock.com"))
            {
                hostName = "Qiwi.gg / SpyderRock";
                isDirectArchive = true;
                priority = 5;
                return true;
            }
            // Priority 6: Direct .RAR/.ZIP/.7Z Archive Links
            if (u.EndsWith(".rar") || u.EndsWith(".zip") || u.EndsWith(".7z") || u.Contains(".zip?") || u.Contains(".rar?"))
            {
                hostName = "Doğrudan Arşiv (.ZIP/.RAR)";
                isDirectArchive = true;
                priority = 6;
                return true;
            }
            // Priority 7: Online-Fix Torrent / P2P Swarm
            if (u.Contains("uploads.online-fix.me") || u.EndsWith(".torrent"))
            {
                hostName = "Online-Fix P2P Ağı";
                isDirectArchive = true;
                priority = 7;
                return true;
            }
            // Blocked / Interactive Captcha Hosts: Ignore completely so they never cause in-app download failures!
            if (u.Contains("akirabox.to") || u.Contains("akirabox.com") ||
                u.Contains("datanodes.to") || u.Contains("megadb.net") || u.Contains("filekeeper.net"))
            {
                return false;
            }
            return false;
        }
    }
}
