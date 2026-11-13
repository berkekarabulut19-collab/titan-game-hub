using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TitanGameHub
{
    public static class GameDeduplicationEngine
    {
        public static string NormalizeGameTitle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string clean = raw.Trim().ToLowerInvariant();

            // Strip download and repack suffixes
            clean = Regex.Replace(clean, @"\s*(?:free|torrent)\s+download.*$", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\s*\+?\s*bonus\s+osts?\b", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\s*\(v[0-9\.\s_ab-]+\)", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\s*\[v[0-9\.\s_ab-]+\]", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\s*v[0-9]+(\.[0-9]+)*([a-z])?", "", RegexOptions.IgnoreCase);
                clean = Regex.Replace(clean, @"[\s,:;+\-–—]*(?:\d+\.\d[\w\.]*|v\d[\w\.]*|build\s+\d+|update\s+\d+)\s*$", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\s*(repack|fitgirl|dodi|online-fix|all\s+dlcs?|steamrip)\b", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\s*(early\s+access|definitive\s+edition|deluxe\s+edition|remastered|goty\s+edition)\b", "", RegexOptions.IgnoreCase);

            // Sürüm / DLC / paket etiketleri (site başlıkları çok süslü geliyor)
            clean = Regex.Replace(clean, @"[\s,\+]*\d+\s*dlcs?\s*/?\s*bonuses?.*$", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\s*&\s*soundtrack\s*bundle.*$", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"(digital\s+deluxe|deluxe|ultimate|premium|gold|complete|collector'?s|enhanced|definitive|goty|game\s+of\s+the\s+year|anniversary)(\s+edition)?", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"(bundle|collection|pack).*$", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"multi?\d+", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"(elamigos|tenoke|flt|rune|codex|plaza|skidrow|reloaded|dodi|fitgirl|online-?fix|empress|goldberg)", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\([^\)]*\)", " ", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"[–—-]\s*v?\d+\.\d+.*$", "", RegexOptions.IgnoreCase);
            // Kuyruk temizliği: ", v142058 (v1.5.3) + v66884 + 6 DLCs/Bonuses" gibi artıklar
            for (int guard = 0; guard < 6; guard++)
            {
                string before = clean;
                clean = Regex.Replace(clean, @"[\s,:;\+\-–—]*\d+\s*dlcs?\s*/?\s*bonuses?.*$", "", RegexOptions.IgnoreCase);
                clean = Regex.Replace(clean, @"[\s,:;\+[\-]*(\d+\\.\d[\w\\.]*|v\d[\w\\.]*|build\s+\d+|update\s+\d+)\s*$", "", RegexOptions.IgnoreCase);
                clean = Regex.Replace(clean, @"[\s,:;\+\-–—]+$", "");
                clean = Regex.Replace(clean, @"\s{2,}", " ");
                if (clean == before) break;
            }

            // Kalan süs kelimeleri (digital edition / bonus / dlc) at
            clean = Regex.Replace(clean, @"(?i)(?:^|\s)(digital|edition|deluxe|ultimate|premium|gold|complete|bonus|bonuses|dlc|dlcs|bundle)(?:$|\s)", " ");
            clean = Regex.Replace(clean, @"\s{2,}", " ");


            // Strip non-alphanumeric characters
            clean = Regex.Replace(clean, @"[^a-z0-9]", "");
            return clean.Trim();
        }

        public static List<GameProfile> Deduplicate(IEnumerable<GameProfile> profiles)
        {
            if (profiles == null) return new List<GameProfile>();
            Dictionary<string, GameProfile> map = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);

            foreach (GameProfile p in profiles)
            {
                if (p == null) continue;
                string key = NormalizeGameTitle(p.Name);
                if (string.IsNullOrEmpty(key)) key = p.Id ?? Guid.NewGuid().ToString("N");

                if (!map.ContainsKey(key))
                {
                    map[key] = p;
                }
                else
                {
                    GameProfile existing = map[key];

                    // Rule 1: Yolu olan (kurulu) kayıt her zaman öncelikli.
                    bool existingHasPath = existing.IsInstalled || !string.IsNullOrEmpty(existing.ExecutablePath);
                    bool newHasPath = p.IsInstalled || !string.IsNullOrEmpty(p.ExecutablePath);
                    if (!existingHasPath && newHasPath)
                    {
                        if (string.IsNullOrEmpty(p.BannerUrl) && !string.IsNullOrEmpty(existing.BannerUrl))
                            p.BannerUrl = existing.BannerUrl;
                        if (string.IsNullOrEmpty(p.DirectMirrorUrl) && !string.IsNullOrEmpty(existing.DirectMirrorUrl))
                            p.DirectMirrorUrl = existing.DirectMirrorUrl;
                        map[key] = p;
                        continue;
                    }

                    // Rule 2: If existing is installed, keep it and merge remote links
                    if (existing.IsInstalled)
                    {
                        if (string.IsNullOrEmpty(existing.DirectMirrorUrl) && !string.IsNullOrEmpty(p.DirectMirrorUrl))
                            existing.DirectMirrorUrl = p.DirectMirrorUrl;
                        if (!string.IsNullOrEmpty(p.RemoteVersion))
                            existing.RemoteVersion = p.RemoteVersion;
                        continue;
                    }

                    // Rule 3: Prefer profile with clean, working direct mirror / magnet URL (ignore ad shorteners)
                    bool existingWeak = string.IsNullOrEmpty(existing.DirectMirrorUrl) || DownloadHostPolicy.IsAdShortener(existing.DirectMirrorUrl);
                    bool newClean = !string.IsNullOrEmpty(p.DirectMirrorUrl) && !DownloadHostPolicy.IsAdShortener(p.DirectMirrorUrl);
                    if (newClean && (existingWeak || DownloadHostPolicy.SpeedScore(p.DirectMirrorUrl) > DownloadHostPolicy.SpeedScore(existing.DirectMirrorUrl)))
                    {
                        existing.DirectMirrorUrl = p.DirectMirrorUrl;
                        if (!string.IsNullOrEmpty(p.DownloadUrl)) existing.DownloadUrl = p.DownloadUrl;
                    }

                    // Rule 4: Prefer profile with authentic cover art
                    if ((string.IsNullOrEmpty(existing.BannerUrl) || existing.BannerUrl.Contains("capsule_231x87")) &&
                        !string.IsNullOrEmpty(p.BannerUrl) && !p.BannerUrl.Contains("capsule_231x87"))
                    {
                        existing.BannerUrl = p.BannerUrl;
                        existing.CapsuleUrl = p.CapsuleUrl;
                    }

                    // Rule 5: Keep the more complete description
                    if ((string.IsNullOrEmpty(existing.Description) || existing.Description.Length < 30) &&
                        !string.IsNullOrEmpty(p.Description) && p.Description.Length > (existing.Description ?? "").Length)
                    {
                        existing.Description = p.Description;
                    }
                }
            }

            return new List<GameProfile>(map.Values);
        }
    }
}
