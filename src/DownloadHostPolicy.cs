using System;
using System.Text.RegularExpressions;

namespace TitanGameHub
{
    /// <summary>
    /// Host davranış ölçümleri (gerçek testlerle):
    ///  - pixeldrain.com : HTTP 206 Range destekli, uygulama içi doğrudan indirme ÇALIŞIYOR (1.4 – 4.3 MB/s)
    ///  - itsfileditchfiles / fileditchfiles / bzzhr / buzzheavier / fuckingfast / qiwi :
    ///    doğrudan istekte 403 (bot koruması) → yalnızca gerçek tarayıcı oturumu ile iner
    ///  - megadb / datanodes / vikingfile / 1fichier : HTML döner, sayfa ayrıştırma + token gerekir
    ///  - gofile.io : API tabanlı (token), ağ tarafında zaman zaman erişilemiyor
    ///
    /// Bu sınıf, ayna seçimini "en hızlı ve en güvenli" sıraya koyar ve uygulama içi
    /// indirme yapılamayan hostlarda tarayıcı oturumlu indirmeye geçilmesini sağlar.
    /// </summary>
    public static class DownloadHostPolicy
    {
        public static string HostLabel(string url)
        {
            try
            {
                Uri u = new Uri(url.StartsWith("//") ? "https:" + url : url);
                return u.Host.Replace("www.", "");
            }
            catch { return "Sunucu"; }
        }

        /// <summary>pixeldrain gibi paylaşım linklerini doğrudan indirme API'sine çevirir.</summary>
        public static string NormalizeDirectUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            try
            {
                string u = url.StartsWith("//") ? "https:" + url : url;

                // pixeldrain: /u/<id> veya /l/<id> → /api/file/<id>?download
                Match m = Regex.Match(u, @"pixeldrain\.com/(?:u|l)/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    string id = m.Groups[1].Value;
                    return string.Format("https://pixeldrain.com/api/file/{0}?download", id);
                }
                return u;
            }
            catch { return url; }
        }

        /// <summary>Reklam kısaltıcı linkler (srnky, shrinkme, clksz vb.) indirme aynası değildir.</summary>
        public static bool IsAdShortener(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string u = url.ToLowerInvariant();
            return u.Contains("srnky.com")
                || u.Contains("shrinkme.")
                || u.Contains("clksz.com")
                || u.Contains("ouo.io")
                || u.Contains("ouo.press")
                || u.Contains("exe.io")
                || u.Contains("fc.lc")
                || u.Contains("linkvertise.")
                || u.Contains("adf.ly")
                || u.Contains("shorte.st")
                || u.Contains("clk.sh")
                || u.Contains("cuty.io")
                || u.Contains("za.gl");
        }

        /// <summary>Yüksek = daha hızlı/güvenilir doğrudan indirme.</summary>
        public static int SpeedScore(string url)
        {
            if (string.IsNullOrEmpty(url)) return 0;
            if (IsAdShortener(url)) return 0;
            string u = url.ToLowerInvariant();

            if (u.Contains("pixeldrain.com")) return 100;
            if (u.Contains("gofile.io")) return 60;                  // API ile çözülebilir
            if (u.Contains("vikingfile") || u.Contains("vik1ngfile")) return 30;
            if (u.Contains("megadb.net") || u.Contains("datanodes.to")) return 25;
            if (u.Contains("1fichier.com")) return 20;
            if (NeedsBrowserSession(u)) return 10;                    // sadece tarayıcıdan iner
            if (u.EndsWith(".rar") || u.Contains(".rar?") || u.Contains(".zip") || u.Contains(".7z")) return 40;
            return 15;
        }

        /// <summary>Doğrudan HTTP isteği 403 dönen, sadece tarayıcı oturumunda inen hostlar.</summary>
        public static bool NeedsBrowserSession(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string u = url.ToLowerInvariant();
            return u.Contains("fileditch")
                || u.Contains("bzzhr.")
                || u.Contains("buzzheavier")
                || u.Contains("fuckingfast")
                || u.Contains("qiwi.gg")
                || u.Contains("drop.download")
                || u.Contains("spyderrock")
                || u.Contains("akirabox");
        }
    }
}
