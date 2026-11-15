using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TitanGameHub
{
    public static class GameDeduplicationEngine
    {
        private static readonly Dictionary<string, string> NormCache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Regex RxFreeTorrent = new Regex(@"\s*(?:free|torrent)\s+download.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxBrackets = new Regex(@"\[[^\]]*\]", RegexOptions.Compiled);
        private static readonly Regex RxAmpBundle = new Regex(@"\s*&\s*(?:all\s+dlcs?|soundtrack|bonus(?:\s+ost)?)\s*(?:bundle)?.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxPlusDlc = new Regex(@"\s*\+\s*\d*\s*dlcs?.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxPlusAllDlc = new Regex(@"\s*\+\s*all\s+dlcs?.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxPlusMp = new Regex(@"\s*\+\s*(?:multiplayer|online|co-?op|lan|bonuses?|soundtrack|ost).*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxDashVer = new Regex(@"\s*[–—-]\s*(?:v\d|build\s+\d|cl#\d|hotfix).*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxParenTags = new Regex(@"\s*\((?:v\d|build\s+\d|repack|fitgirl|dodi|gog|steamrip|early\s+access|multi\d*)[^\)]*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxEditions = new Regex(@"[:\s,-]*(?:digital\s+deluxe|deluxe|ultimate|complete|supporter'?s?|collector'?s?|enhanced|definitive|goty|game\s+of\s+the\s+year|anniversary|infernal|gold|premium|special|remastered|legacy|royal|master|standard)\s+edition\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxDirectorsCut = new Regex(@"[:\s,-]+director'?s\s+cut\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxDashEdition = new Regex(@"[:\s]*[–—-]\s*(?:digital\s+deluxe|deluxe|ultimate|complete|supporter'?s?|collector'?s?|definitive|goty|infernal|remastered)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxSceneGroups = new Regex(@"\b(?:repack|fitgirl|dodi|online-?fix|steamrip|elamigos|tenoke|flt|rune|codex|plaza|skidrow|reloaded|empress|goldberg|xatab|kaos|razor1911|p2p|gog|multi\d+|all\s+dlcs?|early\s+access)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxVerToken = new Regex(@"\bv\d+(?:\.\d+)*[a-z]?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxBuildToken = new Regex(@"\b(?:build|update|hotfix)\s*\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxClToken = new Regex(@"\bcl#\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxNonAlnum = new Regex(@"[^a-z0-9]", RegexOptions.Compiled);

        private static string FoldAscii(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            string pre = input.Replace('Ø', 'O').Replace('ø', 'o').Replace('Æ', 'A').Replace('æ', 'a').Replace('Ł', 'L').Replace('ł', 'l');
            string normalized = pre.Normalize(NormalizationForm.FormKD);
            StringBuilder sb = new StringBuilder(normalized.Length);
            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString();
        }

        public static string NormalizeGameTitle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            lock (NormCache)
            {
                string cached;
                if (NormCache.TryGetValue(raw, out cached))
                    return cached;
            }

            string clean = FoldAscii(raw.Trim()).ToLowerInvariant()
                .Replace('\u2019', '\'').Replace('\u2018', '\'').Replace('\u201C', '"').Replace('\u201D', '"');

            clean = RxFreeTorrent.Replace(clean, "");
            clean = RxBrackets.Replace(clean, " ");
            clean = RxAmpBundle.Replace(clean, "");
            clean = RxPlusDlc.Replace(clean, "");
            clean = RxPlusAllDlc.Replace(clean, "");
            clean = RxPlusMp.Replace(clean, "");
            clean = RxDashVer.Replace(clean, "");
            clean = RxParenTags.Replace(clean, " ");
            clean = RxEditions.Replace(clean, "");
            clean = RxDirectorsCut.Replace(clean, "");
            clean = RxDashEdition.Replace(clean, "");
            clean = RxSceneGroups.Replace(clean, " ");
            clean = RxVerToken.Replace(clean, " ");
            clean = RxBuildToken.Replace(clean, " ");
            clean = RxClToken.Replace(clean, " ");
            clean = RxNonAlnum.Replace(clean, "").Trim();

            lock (NormCache)
            {
                if (NormCache.Count > 40000) NormCache.Clear();
                NormCache[raw] = clean;
            }
            return clean;
        }

        private static void MergeEditionAndDlcMetadata(GameProfile target, GameProfile source)
        {
            if (target == null || source == null) return;
            string combined = (source.Name ?? "") + " | " + (source.Version ?? "") + " | " + (target.Version ?? "");
            List<string> tags = new List<string>();
            if (!string.IsNullOrWhiteSpace(target.Version) &&
                !target.Version.Equals("Full Version", StringComparison.OrdinalIgnoreCase) &&
                !target.Version.Equals("Son Sürüm", StringComparison.OrdinalIgnoreCase))
            {
                string[] existingParts = target.Version.Split(new string[] { " • " }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < existingParts.Length; i++)
                    tags.Add(existingParts[i].Trim());
            }

            Match verM = Regex.Match(combined, @"\b(v\d+\.\d+(?:\.\d+)*[a-z]?)\b", RegexOptions.IgnoreCase);
            if (verM.Success && tags.Count == 0)
                tags.Add(verM.Groups[1].Value);

            Match dlcM = Regex.Match(combined, @"(\+?\s*\d+\s*DLCs?|All\s+DLCs?)", RegexOptions.IgnoreCase);
            if (dlcM.Success)
            {
                string dlcTag = Regex.Replace(dlcM.Groups[1].Value.Trim(), @"\s+", " ").Replace("DLCs", "DLC");
                if (dlcTag.StartsWith("+ ")) dlcTag = "+" + dlcTag.Substring(2);
                if (!dlcTag.StartsWith("+") && char.IsDigit(dlcTag[0])) dlcTag = "+" + dlcTag;
                bool hasDlc = false;
                for (int i = 0; i < tags.Count; i++)
                    if (tags[i].IndexOf("DLC", StringComparison.OrdinalIgnoreCase) >= 0) hasDlc = true;
                if (!hasDlc) tags.Add(dlcTag);
            }

            if (Regex.IsMatch(combined, @"\b(Full\s+Version|Full\s+Paket|Complete|All\s+DLCs?|Repack|Deluxe|Ultimate|GOTY)\b", RegexOptions.IgnoreCase))
            {
                bool hasFull = false;
                for (int i = 0; i < tags.Count; i++)
                    if (tags[i].IndexOf("Full Paket", StringComparison.OrdinalIgnoreCase) >= 0) hasFull = true;
                if (!hasFull) tags.Add("Full Paket");
            }

            if (tags.Count > 0)
                target.Version = string.Join(" • ", tags.ToArray());
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
                        MergeEditionAndDlcMetadata(p, existing);
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
                        MergeEditionAndDlcMetadata(existing, p);
                        continue;
                    }

                    MergeEditionAndDlcMetadata(existing, p);

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
