using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace TitanGameHub
{
    /// <summary>
    /// Kendi katalog API'niz (Cloudflare Worker / VPS) üzerinden katalog çekme.
    /// titan_user.cfg: CATALOG_API=https://ornek.workers.dev
    ///
    /// Sözleşme: GET {base}/catalog?page=1&per=50
    ///   {"games":[{"id","name","version","size","cover","url","appid","category"}]}
    ///
    /// Tanımlı değilse veya hata verirse null döner; uygulama scraping yolunu kullanır.
    /// </summary>
    public static class CatalogApiClient
    {
        public static string GetBaseUrl()
        {
            try
            {
                string cfg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "titan_user.cfg");
                if (!File.Exists(cfg)) return null;
                foreach (string raw in File.ReadAllLines(cfg))
                {
                    string line = raw.Trim();
                    if (!line.StartsWith("CATALOG_API", StringComparison.OrdinalIgnoreCase)) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string v = line.Substring(eq + 1).Trim().TrimEnd('/');
                    if (v.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return v;
                }
            }
            catch { }
            return null;
        }

        public static bool IsConfigured { get { return !string.IsNullOrEmpty(GetBaseUrl()); } }

        public static List<GameProfile> Fetch(int page, int perPage, out string status)
        {
            status = "";
            string baseUrl = GetBaseUrl();
            if (string.IsNullOrEmpty(baseUrl)) return null;

            try
            {
                string url = baseUrl + "/catalog?page=" + page + "&per=" + perPage;
                string json;
                using (WebClient wc = new WebClient())
                {
                    wc.Proxy = null;
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers.Add("User-Agent", "TitanGameHub/1.0");
                    json = wc.DownloadString(url);
                }

                List<GameProfile> list = new List<GameProfile>();
                foreach (string obj in SplitObjects(json))
                {
                    GameProfile gp = new GameProfile();
                    gp.Id = Field(obj, "id");
                    gp.Name = Field(obj, "name");
                    if (string.IsNullOrEmpty(gp.Name)) continue;

                    gp.Version = Field(obj, "version");
                    gp.RemoteVersion = gp.Version;
                    gp.FileSize = Field(obj, "size");
                    gp.BannerUrl = Field(obj, "cover");
                    gp.CapsuleUrl = gp.BannerUrl;
                    gp.DownloadUrl = Field(obj, "url");
                    gp.SteamAppId = Field(obj, "appid");
                    gp.Category = Field(obj, "category");
                    gp.Description = Field(obj, "description");

                    if (string.IsNullOrEmpty(gp.SteamAppId)) gp.SteamAppId = "480";
                    if (string.IsNullOrEmpty(gp.FileSize)) gp.FileSize = "Bilinmiyor";
                    list.Add(gp);
                }

                status = string.Format("☁️ Katalog API: {0} oyun (sayfa {1})", list.Count, page);
                return list;
            }
            catch (Exception ex)
            {
                status = "Katalog API hatası: " + ex.Message;
                return null;
            }
        }

        /// <summary>Basit JSON nesne ayırıcı (iç içe süslü parantez desteği).</summary>
        private static List<string> SplitObjects(string json)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(json)) return list;

            int depth = 0;
            int start = -1;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (c == '"')
                {
                    // string içini atla
                    i++;
                    while (i < json.Length)
                    {
                        if (json[i] == '\\') { i += 2; continue; }
                        if (json[i] == '"') break;
                        i++;
                    }
                    continue;
                }

                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        list.Add(json.Substring(start, i - start + 1));
                        start = -1;
                    }
                }
            }
            return list;
        }

        /// <summary>Basit alan okuyucu: "name":"değer" veya "name":123</summary>
        private static string Field(string json, string name)
        {
            try
            {
                string needle = "\"" + name + "\"";
                int i = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return "";
                i += needle.Length;

                while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == '\n' || json[i] == '\r')) i++;
                if (i >= json.Length || json[i] != ':') return "";
                i++;
                while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == '\n' || json[i] == '\r')) i++;
                if (i >= json.Length) return "";

                if (json[i] == '"')
                {
                    StringBuilder sb = new StringBuilder();
                    i++;
                    while (i < json.Length)
                    {
                        char c = json[i];
                        if (c == '\\' && i + 1 < json.Length) { sb.Append(json[i + 1]); i += 2; continue; }
                        if (c == '"') break;
                        sb.Append(c);
                        i++;
                    }
                    return sb.ToString();
                }

                int end = i;
                while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-' || json[end] == '.' || json[end] == 'e' || json[end] == 'E')) end++;
                return json.Substring(i, Math.Max(0, end - i));
            }
            catch { }
            return "";
        }
    }
}
