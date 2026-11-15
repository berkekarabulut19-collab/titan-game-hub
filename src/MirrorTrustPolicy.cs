using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TitanGameHub
{
    /// <summary>
    /// Katalogdaki statik ayna linkleri güvenilmez olabiliyor:
    ///  - Aynı link birden fazla oyuna yapıştırılmış (placeholder) → yanlış oyunu indirir.
    ///  - Link başka bir oyuna ait olabiliyor (ör. R.E.P.O. kaydında "Lawn.Mowing.Simulator").
    /// Bu sınıf canlı site taramasından gelen aynaları önceliklendirir ve dosya adından
    /// oyunun kendisine ait olmadığı açıkça anlaşılan linkleri eler.
    /// </summary>
    public static class MirrorTrustPolicy
    {
        private static readonly string[] GenericTokens = new string[]
        {
            "rar", "zip", "7z", "part", "part1", "part2", "part3", "www", "http", "https",
            "steamrip", "com", "net", "org", "online", "fix", "repack", "fitgirl", "dodi",
            "elamigos", "gofile", "fileditch", "fileditchfiles", "itsfileditchfiles", "bzzhr",
            "buzzheavier", "pixeldrain", "download", "file", "files", "alpha", "beta", "balpha",
            "balpha28", "balpha33", "balpha39", "balpha42", "upload", "uploads", "data", "dl"
        };

        private static readonly object _cacheLock = new object();
        private static Dictionary<string, int> _urlUsageCache = null;
        private static DateTime _cacheStamp = DateTime.MinValue;

        /// <summary>Katalogda aynı ayna URL'si birden fazla oyunda kullanılıyorsa placeholder'dır.</summary>
        public static bool IsSharedPlaceholder(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            Dictionary<string, int> usage = GetUrlUsage();
            string key = url.Trim().ToLowerInvariant();
            int count;
            return usage.TryGetValue(key, out count) && count > 1;
        }

        /// <summary>Dosya adı oyunla hiçbir ortak kelime paylaşmıyorsa yanlış oyun paketi olabilir.</summary>
        public static bool LooksLikeWrongGame(string url, string gameName)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(gameName)) return false;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false; // steam:// vb. bizi ilgilendirmez
            if (url.IndexOf("gameloop.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                url.IndexOf("com.tencent.ig", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            string fileName = ExtractCandidateFileName(url);
            if (!IsTitleLikeFileName(fileName)) return false; // gofile/d/AbC123 gibi ID tabanlı linkler

            HashSet<string> fileTokens = Tokenize(fileName);
            HashSet<string> gameTokens = Tokenize(gameName);
            if (gameTokens.Count == 0) return false;
            if (fileTokens.Count == 0) return false;

            foreach (string ft in fileTokens)
            {
                foreach (string gt in gameTokens)
                {
                    if (ft == gt) return false;
                    if (ft.Length >= 5 && gt.Length >= 5 && Levenshtein(ft, gt) <= 2) return false;
                    // "liarsbarsteamripcomrar" vs "liarsbar" -> bitişik yazım eşleşmesi
                    if (ft.Length >= 4 && gt.Length >= 4 &&
                        (ft.StartsWith(gt, StringComparison.Ordinal) || gt.StartsWith(ft, StringComparison.Ordinal)))
                        return false;
                }
            }
            return true;
        }

        /// <summary>URL yolundan veya sorgu parametresinden (ör. file.php?f=/x/Y.rar) en iyi dosya adını çıkarır.</summary>
        private static string ExtractCandidateFileName(string url)
        {
            List<string> candidates = new List<string>();
            try
            {
                string pathOnly = url;
                string query = "";
                int q = url.IndexOf('?');
                if (q > 0)
                {
                    pathOnly = url.Substring(0, q);
                    query = url.Substring(q + 1);
                }

                string fromPath = Path.GetFileName(pathOnly);
                if (!string.IsNullOrEmpty(fromPath)) candidates.Add(Uri.UnescapeDataString(fromPath));

                if (!string.IsNullOrEmpty(query))
                {
                    foreach (string pair in query.Split('&'))
                    {
                        int eq = pair.IndexOf('=');
                        if (eq <= 0) continue;
                        string val = Uri.UnescapeDataString(pair.Substring(eq + 1));
                        string vn = Path.GetFileName(val);
                        if (!string.IsNullOrEmpty(vn)) candidates.Add(vn);
                    }
                }
            }
            catch { }

            string best = "";
            foreach (string c in candidates)
            {
                if (IsTitleLikeFileName(c) && c.Length > best.Length) best = c;
            }
            if (best.Length > 0) return best;
            return candidates.Count > 0 ? candidates[0] : "";
        }

        /// <summary>Dosya adı gerçek bir isim mi, yoksa host'un ürettiği kısa ID mi?</summary>
        private static bool IsTitleLikeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            if (fileName.Length < 6) return false;
            bool hasExtension = fileName.IndexOf('.') > 0;
            bool hasSeparator = fileName.IndexOf('-') >= 0 || fileName.IndexOf('_') >= 0 || fileName.IndexOf(' ') >= 0;
            if (!hasExtension && !hasSeparator && fileName.Length < 16) return false; // "jMI5xb", "smYFH38EBZ"
            return true;
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
                    int del = prev[j] + 1;
                    int ins = cur[j - 1] + 1;
                    int sub = prev[j - 1] + cost;
                    cur[j] = Math.Min(Math.Min(del, ins), sub);
                }
                int[] t = prev; prev = cur; cur = t;
            }
            return prev[b.Length];
        }

        private static HashSet<string> Tokenize(string raw)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(raw)) return set;

            string lower = raw.ToLowerInvariant();
            lower = Regex.Replace(lower, @"\.(rar|zip|7z|torrent|exe|html)$", " ");
            string[] parts = Regex.Split(lower, @"[^a-z0-9]+");

            foreach (string p in parts)
            {
                if (p.Length < 4) continue;
                if (GenericTokens.Contains(p)) continue;
                if (Regex.IsMatch(p, @"^[0-9a-f]{8,}$")) continue; // hash parçası
                set.Add(p);
            }

            // "R.E.P.O." gibi noktalı kısaltmalar tek harfe bölünür; bitişik hali de token sayılır.
            string compact = Regex.Replace(lower, @"[^a-z0-9]+", "");
            if (compact.Length >= 4 && compact.Length <= 24 && !GenericTokens.Contains(compact))
                set.Add(compact);

            return set;
        }

        private static Dictionary<string, int> GetUrlUsage()
        {
            lock (_cacheLock)
            {
                if (_urlUsageCache != null && (DateTime.UtcNow - _cacheStamp).TotalSeconds < 60)
                    return _urlUsageCache;

                Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string cfg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "games_v6.cfg");
                    if (File.Exists(cfg))
                    {
                        foreach (string line in File.ReadAllLines(cfg))
                        {
                            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                            string[] p = line.Split('|');
                            if (p.Length < 16) continue;
                            string u = (p[15] ?? "").Trim().ToLowerInvariant();
                            if (u.Length < 8 || !u.StartsWith("http")) continue;
                            int c;
                            map.TryGetValue(u, out c);
                            map[u] = c + 1;
                        }
                    }
                }
                catch { }

                _urlUsageCache = map;
                _cacheStamp = DateTime.UtcNow;
                return map;
            }
        }

        /// <summary>
        /// Ayna kalite puanı: gerçek arşiv dosyası (yolunda .rar/.zip olan) klasör linkinden iyidir.
        /// gofile.io/d/&lt;id&gt; gibi klasör linkleri birden fazla oyunu işaret edebiliyor.
        /// </summary>
        public static int ScoreUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return 0;
            string u = url.ToLowerInvariant();
            int score = 1;
            if (u.Contains(".rar") || u.Contains(".zip") || u.Contains(".7z")) score += 5;
            if (u.Contains("fileditch") || u.Contains("bzzhr") || u.Contains("buzzheavier")) score += 2;
            if (u.Contains("pixeldrain.com/u/")) score += 2;
            if (u.Contains("gofile.io/d/")) score += 0; // klasör: en düşük öncelik
            if (u.Contains("drop.download") || u.Contains("vikingfile") || u.Contains("vik1ngfile")) score -= 2;
            return score;
        }

        /// <summary>Neden elendiğini insan dilinde döndürür (log/UI için).</summary>
        public static string DescribeVerdict(string url, GameProfile gp, out bool accept)
        {
            accept = true;
            if (string.IsNullOrEmpty(url)) { accept = false; return "boş link"; }

            if (gp != null && LooksLikeWrongGame(url, gp.Name))
            {
                accept = false;
                return "dosya adı bu oyuna ait görünmüyor";
            }
            if (IsSharedPlaceholder(url))
            {
                accept = false;
                return "katalogda birden fazla oyunda aynı link (placeholder)";
            }
            return "kabul";
        }
    }
}
