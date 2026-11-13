using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace TitanGameHub
{
    /// <summary>
    /// URL'den oyun ekleme: kullanıcı bir SteamRIP / FitGirl / OvaGames sayfa linki
    /// yapıştırır; uygulama başlık, sürüm, boyut, AppID ve KAPAK görselini çekip kütüphaneye ekler.
    /// </summary>
    public static class SiteUrlImporter
    {
        private static readonly string[] AllowedHosts = new string[]
        {
            "steamrip.com", "www.steamrip.com",
            
            "fitgirl-repacks.site", "www.fitgirl-repacks.site",
            "ovagames.com", "www.ovagames.com",
            "freegogpcgames.com", "www.freegogpcgames.com",
            "crotorrents.com", "www.crotorrents.com",
            "online-fix.me", "www.online-fix.me"
        };

        static SiteUrlImporter()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        }

        public static bool IsSupportedHost(string url)
        {
            try
            {
                Uri u = new Uri(url.StartsWith("//") ? "https:" + url : url);
                string host = u.Host.ToLowerInvariant();
                foreach (string h in AllowedHosts)
                    if (host == h || host.EndsWith("." + h, StringComparison.Ordinal)) return true;
                return false;
            }
            catch { return false; }
        }

        public static GameProfile Import(string url, out string statusMessage)
        {
            statusMessage = "";

            if (string.IsNullOrWhiteSpace(url))
            {
                statusMessage = "URL boş.";
                return null;
            }

            url = url.Trim();
            if (url.StartsWith("//")) url = "https:" + url;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;

            if (!IsSupportedHost(url))
            {
                statusMessage = "Desteklenmeyen veya geçersiz bağlantı adresi.";
                return null;
            }

            string html;
            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Proxy = null;
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
                    wc.Headers.Add("Accept", "text/html,application/xhtml+xml");
                    wc.Headers.Add("Accept-Language", "en-US,en;q=0.9");
                    html = wc.DownloadString(url);
                }
            }
            catch
            {
                // Cloudflare 403 vb. durumlarda Jina Reader üzerinden metin çekmeyi dene
                html = FetchViaJina(url);
                if (string.IsNullOrEmpty(html))
                {
                    statusMessage = "Sayfa içeriği şu an alınamadı (site koruma altında veya erişilemiyor).";
                    return null;
                }
            }

            if (string.IsNullOrEmpty(html) || html.Length < 500)
            {
                statusMessage = "Sayfa içeriği alınamadı (site boş/engelli yanıt verdi).";
                return null;
            }

            GameProfile gp = new GameProfile();

            // --- İsim ---
            string title = ExtractMeta(html, "og:title");
            if (string.IsNullOrEmpty(title)) title = ExtractTag(html, "title");
            title = CleanTitle(title);
            if (string.IsNullOrEmpty(title)) title = CleanTitle(ExtractMarkdownTitle(html));
            if (string.IsNullOrEmpty(title))
            {
                statusMessage = "Oyun adı sayfadan çıkarılamadı (sayfa tanınmadı).";
                return null;
            }

            gp.Id = BuildId(url, title);
            gp.Name = title;

            // --- Sürüm ---
            string version = ExtractVersion(html, title);

            // --- Boyut ---
            string size = ExtractSize(html);

            // --- AppID ---
            string appId = ExtractAppId(html);

            // --- Kategori / açıklama ---
            string desc = ExtractMeta(html, "og:description");
            if (string.IsNullOrEmpty(desc)) desc = ExtractMeta(html, "description");
            desc = Regex.Replace(desc ?? "", @"\s{2,}", " ").Trim();
            if (desc.Length > 220) desc = desc.Substring(0, 217) + "...";

            // --- Kapak ---
            string cover = ExtractTopLevelMeta(html, "og:image");
            if (string.IsNullOrEmpty(cover)) cover = ExtractLargestContentImage(html);

            // --- Doğrudan arşiv linki (varsa) ---
            string direct = MultiSourceVersionResolver.PickBestArchiveLink(html, false);

            gp.Version = string.IsNullOrEmpty(version) ? "v1.0" : version;
            gp.RemoteVersion = gp.Version;
            gp.FileSize = string.IsNullOrEmpty(size) ? "Bilinmiyor" : size;
            gp.SteamAppId = string.IsNullOrEmpty(appId) ? "480" : appId;
            gp.DownloadUrl = url;
            gp.OnlineFixUrl = "https://online-fix.me/index.php?do=search&subaction=search&story=" + Uri.EscapeDataString(gp.Name);
            gp.Description = string.IsNullOrEmpty(desc) ? (gp.Name + " — eklenen katalog kaydı.") : desc;
            gp.MaxPlayers = "Online Co-Op";
            gp.Category = GuessCategory(url);
            gp.BannerUrl = cover;
            gp.CapsuleUrl = cover;
            gp.DirectMirrorUrl = direct;
            gp.ExecutablePath = "";
            gp.WorkingDirectory = "";

            var result = new StringBuilder();
            result.Append("✔ ").Append(gp.Name).Append(" eklendi.\n");
            result.Append("• Sürüm: ").Append(gp.Version).Append("\n");
            result.Append("• Boyut: ").Append(gp.FileSize).Append("\n");
            result.Append("• AppID: ").Append(gp.SteamAppId).Append("\n");
            result.Append("• Kapak: ").Append(string.IsNullOrEmpty(cover) ? "bulunamadı (monogram gösterilecek)" : "alındı").Append("\n");
            result.Append("• Doğrudan arşiv: ").Append(string.IsNullOrEmpty(direct) ? "sayfadan çözülecek" : "bulundu");
            statusMessage = result.ToString();
            return gp;
        }

        /// <summary>Jina Reader / markdown çıktısından başlık çıkarır: "# Valheim" veya "Title: Valheim".</summary>
        private static string ExtractMarkdownTitle(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            Match h = Regex.Match(text, @"^#{1,2}\s+(.{3,120})$", RegexOptions.Multiline);
            if (h.Success) return h.Groups[1].Value.Trim();

            Match t = Regex.Match(text, @"(?im)^\s*Title:\s*(.{3,120})$");
            if (t.Success) return t.Groups[1].Value.Trim();

            string[] lines = text.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string l = line.Trim();
                if (l.Length >= 4 && l.Length <= 90 && !l.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                    !l.StartsWith("!") && !l.StartsWith("|") && !l.StartsWith("*"))
                    return l;
            }
            return null;
        }

        private static string FetchViaJina(string url)
        {
            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Proxy = null;
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    return wc.DownloadString("https://r.jina.ai/" + url);
                }
            }
            catch { return null; }
        }

        private static string BuildId(string url, string title)
        {
            try
            {
                Uri u = new Uri(url);
                string slug = Regex.Replace(u.AbsolutePath.Trim('/'), @"[^\w\-]", "-");
                slug = Regex.Replace(slug, @"-{2,}", "-").Trim('-');
                slug = Regex.Replace(slug, @"-(html?|php|aspx)$", "", RegexOptions.IgnoreCase);
                if (slug.Length > 40) slug = slug.Substring(0, 40);
                if (slug.Length > 3) return slug.ToLowerInvariant();
            }
            catch { }

            string id = Regex.Replace(title.ToLowerInvariant(), @"[^\w]+", "-").Trim('-');
            return id.Length > 40 ? id.Substring(0, 40) : id;
        }

        private static string GuessCategory(string url)
        {
            string h = url.ToLowerInvariant();
            if (h.Contains("steamrip")) return "Katalog • Tam Sürüm";
            
            if (h.Contains("fitgirl")) return "Katalog • Repack";
            if (h.Contains("ovagames")) return "Katalog • Repack";
            if (h.Contains("freegog")) return "Katalog • DRM-Free";
            if (h.Contains("online-fix")) return "Katalog • Co-Op";
            return "Katalog";
        }

        private static string CleanTitle(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string s = WebUtility.HtmlDecode(raw).Trim();

            s = Regex.Replace(s, @"\[[^\]]*\]|\([^\)]*\)", " ");                       // [v1.2 + Co-op], (2024)
            s = Regex.Replace(s, @"(?i)free\s+download|pre-?installed|repack", " ");
            s = Regex.Replace(s, @"(?i)\s*[\|\-–—]\s*(Ova\s*Games|SteamRIP|FitGirl|FreeGOG|Online-?Fix|PC Games)\s*$", " ");
            s = Regex.Replace(s, @"(?i)(MULTi\d+|Multi\d+|-?ElAmigos|-?TENOKE|-?FLT|-?RUNE|-?CODEX|-?PLAZA|-?SKIDROW|-?RELOADED|-?DODI|-?FitGirl)", " ");
            s = Regex.Replace(s, @"(?i)\s*(free download|download)\s*$", " ");
            s = Regex.Replace(s, @"\s{2,}", " ").Trim(' ', '-', ':', '|', ',', '.');
            return s;
        }

        private static string ExtractVersion(string html, string title)
        {
            Match v = Regex.Match(html, @"<strong>\s*Version\s*[:]*\s*</strong>\s*[:]*\s*([^<\r\n]+)", RegexOptions.IgnoreCase);
            if (v.Success) return v.Groups[1].Value.Trim();

            Match tv = Regex.Match(title ?? "", @"(?i)(v?\d+\.\d+(?:\.\d+){0,2})");
            if (tv.Success) return tv.Groups[1].Value;

            Match gv = Regex.Match(html, @"(?i)(?:game\s*version|version)\s*[:<>=/\-strong\s]*v?(\d+\.\d+(?:\.\d+){0,2})");
            if (gv.Success) return "v" + gv.Groups[1].Value;

            Match anyv = Regex.Match(html, @"v(\d+\.\d+(?:\.\d+){0,2})");
            if (anyv.Success) return "v" + anyv.Groups[1].Value;

            Match build = Regex.Match(html, @"(?i)build\s*[:#]?\s*(\d{5,})");
            if (build.Success) return "Build " + build.Groups[1].Value;

            return null;
        }

        private static string ExtractSize(string html)
        {
            Match m = Regex.Match(html, @"(?i)(?:game|file|repack|original|download)\s*size\s*[:<>/\-strong\s]*([0-9\.,]+\s*(?:GB|MB|TB))");
            if (m.Success) return Regex.Replace(m.Groups[1].Value, @"\s+", " ");
            return null;
        }

        private static string ExtractAppId(string html)
        {
            Match m = Regex.Match(html, @"store\.steampowered\.com/app/(\d+)", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value;
            Match m2 = Regex.Match(html, @"""steam_appid""\s*:\s*""?(\d+)", RegexOptions.IgnoreCase);
            if (m2.Success) return m2.Groups[1].Value;
            return null;
        }

        private static string ExtractMeta(string html, string name)
        {
            char q = '"';
            char a = '\'';
            string cls = "[" + q + a + "]";
            string notCls = "[^" + q + a + "]*";

            string p1 = "<meta[^>]+(?:property|name)=" + cls + Regex.Escape(name) + cls + "[^>]*content=" + cls + "(" + notCls + ")" + cls;
            Match m = Regex.Match(html, p1, RegexOptions.IgnoreCase);
            if (m.Success) return WebUtility.HtmlDecode(m.Groups[1].Value);

            string p2 = "<meta[^>]+content=" + cls + "(" + notCls + ")" + cls + "[^>]*(?:property|name)=" + cls + Regex.Escape(name) + cls;
            Match m2 = Regex.Match(html, p2, RegexOptions.IgnoreCase);
            if (m2.Success) return WebUtility.HtmlDecode(m2.Groups[1].Value);

            return null;
        }

        private static string ExtractTopLevelMeta(string html, string name)
        {
            return ExtractMeta(html, name);
        }

        private static string ExtractTag(string html, string tag)
        {
            Match m = Regex.Match(html, "<" + tag + "[^>]*>(.*?)</" + tag + ">", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return m.Success ? WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", "")).Trim() : null;
        }

        /// <summary>Sayfadaki en büyük görseli tahmin eder (logo/ikon hariç).</summary>
        private static string ExtractLargestContentImage(string html)
        {
            MatchCollection imgs = Regex.Matches(html, @"https?://[^""'\s<>]+\.(?:jpg|jpeg|png|webp)", RegexOptions.IgnoreCase);
            string best = null;
            int bestScore = -1;
            foreach (Match m in imgs)
            {
                string u = m.Value;
                string lower = u.ToLowerInvariant();
                if (lower.Contains("logo") || lower.Contains("favicon") || lower.Contains("avatar") ||
                    lower.Contains("icon") || lower.Contains("sprite") || lower.Contains("banner-ad")) continue;

                int score = 1;
                if (lower.Contains("uploads")) score += 3;
                if (lower.Contains("capsule") || lower.Contains("header")) score += 4;
                if (lower.Contains("screenshot")) score -= 1;
                if (Regex.IsMatch(lower, @"-\d{2,4}x\d{2,4}\.")) score -= 1; // küçük thumbnail

                if (score > bestScore) { bestScore = score; best = u; }
            }
            return best;
        }
    }
}
