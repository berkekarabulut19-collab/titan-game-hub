using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace TitanGameHub
{
    /// <summary>
    /// Bağlı sitelerin kataloglarını toplu olarak kütüphaneye aktarır.
    /// Her kaynaktan sayfa sayfa çeker (SteamRIP / FitGirl / OvaGames / FreeGOG),
    /// başlık + sürüm + boyut + KAPAK görseli + ayna linki ile birlikte kaydeder.
    /// Aynı oyun birden fazla sitede varsa tek kayıt tutulur (kapak ve sürüm en iyisi seçilir).
    /// </summary>
    public static class BulkCatalogImporter
    {
        public class ImportSummary
        {
            public int PagesFetched;
            public int Fetched;
            public int Added;
            public int Updated;
            public int Skipped;
            public int WithCover;
            public List<string> Log = new List<string>();
            public string Text
            {
                get
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendFormat("Sayfa: {0} • Çekilen: {1} • Yeni: {2} • Güncellenen: {3} • Atlandı: {4} • Kapaklı: {5}",
                        PagesFetched, Fetched, Added, Updated, Skipped, WithCover);
                    return sb.ToString();
                }
            }
        }

        // (kategoriId, ad) — SiteParserService.FetchLiveSiteGames bu id'leri tanır
        private static readonly int[] SourceIds = new int[] { -11, -12, -13, 0 };
        private static readonly string[] SourceNames = new string[] { "FitGirl", "OvaGames", "FreeGOG", "SteamRIP" };

        public static ImportSummary Import(List<GameProfile> catalog, int pagesPerSource, int perPage, Action<string, int> progress)
        {
            ImportSummary sum = new ImportSummary();
            if (catalog == null) catalog = new List<GameProfile>();

            int totalSteps = Math.Max(1, SourceIds.Length * pagesPerSource);
            int step = 0;

            for (int s = 0; s < SourceIds.Length; s++)
            {
                for (int page = 1; page <= pagesPerSource; page++)
                {
                    if (progress != null)
                        progress(string.Format("{0} • sayfa {1}/{2} taranıyor...", SourceNames[s], page, pagesPerSource),
                                 (int)((step * 100.0) / totalSteps));
                    step++;

                    List<GameProfile> batch = null;
                    try
                    {
                        string status;
                        batch = SiteParserService.FetchLiveSiteGames(SourceIds[s], null, page, perPage, out status);
                        sum.PagesFetched++;
                    }
                    catch { batch = null; }

                    if (batch == null || batch.Count == 0)
                    {
                        sum.Log.Add(SourceNames[s] + " sayfa " + page + ": sonuç yok");
                        break; // bu kaynakta sayfa bitti
                    }

                    foreach (GameProfile live in batch)
                    {
                        sum.Fetched++;
                        if (live == null || string.IsNullOrEmpty(live.Name)) { sum.Skipped++; continue; }

                        GameProfile existing = FindExisting(catalog, live);
                        if (existing == null)
                        {
                            GameProfile gp = new GameProfile
                            {
                                Id = !string.IsNullOrEmpty(live.Id) ? live.Id : GameDeduplicationEngine.NormalizeGameTitle(live.Name),
                                Name = live.Name,
                                Category = string.IsNullOrEmpty(live.Category) ? SourceNames[s] : live.Category,
                                ExecutablePath = "",
                                WorkingDirectory = "",
                                Version = string.IsNullOrEmpty(live.Version) ? "v1.0" : live.Version,
                                RemoteVersion = string.IsNullOrEmpty(live.RemoteVersion) ? (string.IsNullOrEmpty(live.Version) ? "v1.0" : live.Version) : live.RemoteVersion,
                                DownloadUrl = live.DownloadUrl,
                                OnlineFixUrl = string.IsNullOrEmpty(live.OnlineFixUrl)
                                    ? "https://online-fix.me/index.php?do=search&subaction=search&story=" + Uri.EscapeDataString(live.Name)
                                    : live.OnlineFixUrl,
                                SteamAppId = string.IsNullOrEmpty(live.SteamAppId) ? "480" : live.SteamAppId,
                                Description = live.Description,
                                MaxPlayers = string.IsNullOrEmpty(live.MaxPlayers) ? "Online Co-Op" : live.MaxPlayers,
                                BannerUrl = live.BannerUrl,
                                CapsuleUrl = string.IsNullOrEmpty(live.CapsuleUrl) ? live.BannerUrl : live.CapsuleUrl,
                                FileSize = string.IsNullOrEmpty(live.FileSize) ? "Bilinmiyor" : live.FileSize,
                                DirectMirrorUrl = live.DirectMirrorUrl,
                                IsFavorite = false,
                                LastPlayedText = "Henüz Oynanmadı"
                            };
                            catalog.Add(gp);
                            sum.Added++;
                            if (!string.IsNullOrEmpty(gp.BannerUrl)) sum.WithCover++;
                        }
                        else
                        {
                            bool changed = false;

                            // Kapak yoksa gelen kapakla tamamla
                            if (string.IsNullOrEmpty(existing.BannerUrl) && !string.IsNullOrEmpty(live.BannerUrl))
                            {
                                existing.BannerUrl = live.BannerUrl;
                                existing.CapsuleUrl = live.BannerUrl;
                                changed = true;
                                sum.WithCover++;
                            }
                            // Daha yeni sürüm geldiyse güncelle
                            string newVer = !string.IsNullOrEmpty(live.RemoteVersion) ? live.RemoteVersion : live.Version;
                            if (!string.IsNullOrEmpty(newVer) &&
                                MultiSourceVersionResolver.CompareVersions(newVer, existing.RemoteVersion) > 0)
                            {
                                existing.RemoteVersion = newVer;
                                if (!existing.IsInstalled) existing.Version = newVer;
                                changed = true;
                            }
                            // Ayna linki yoksa tamamla
                            if (string.IsNullOrEmpty(existing.DirectMirrorUrl) && !string.IsNullOrEmpty(live.DirectMirrorUrl))
                            {
                                existing.DirectMirrorUrl = live.DirectMirrorUrl;
                                changed = true;
                            }
                            // SteamRIP sayfa adresi yoksa doldur
                            if (string.IsNullOrEmpty(existing.DownloadUrl) && !string.IsNullOrEmpty(live.DownloadUrl))
                            {
                                existing.DownloadUrl = live.DownloadUrl;
                                changed = true;
                            }

                            if (changed) sum.Updated++; else sum.Skipped++;
                        }
                    }

                    sum.Log.Add(string.Format("{0} sayfa {1}: {2} oyun", SourceNames[s], page, batch.Count));
                    Thread.Sleep(250);
                }
            }

            // ===== Kapak tamamlama: afişi olmayan oyunlar için Steam mağaza araması =====
            int fillLimit = 80;
            int filled = 0;
            if (progress != null) progress("Kapaksız oyunlar için afiş aranıyor...", 96);

            foreach (GameProfile gp in catalog)
            {
                if (filled >= fillLimit) break;
                if (gp == null || !string.IsNullOrEmpty(gp.BannerUrl)) continue;

                try
                {
                    string art = null;
                    System.Threading.ManualResetEvent done = new System.Threading.ManualResetEvent(false);
                    SteamArtworkResolver.ResolveAsync(gp.Name, delegate(string url) { art = url; done.Set(); });
                    done.WaitOne(12000);

                    if (!string.IsNullOrEmpty(art))
                    {
                        gp.BannerUrl = art;
                        gp.CapsuleUrl = art;
                        sum.WithCover++;
                        filled++;
                    }
                }
                catch { }
            }
            if (filled > 0) sum.Updated += filled;

            if (progress != null) progress("Katalog kaydediliyor...", 99);
            return sum;
        }

        private static GameProfile FindExisting(List<GameProfile> catalog, GameProfile live)
        {
            string key = GameDeduplicationEngine.NormalizeGameTitle(live.Name);
            foreach (GameProfile gp in catalog)
            {
                if (gp == null) continue;
                if (!string.IsNullOrEmpty(live.Id) && string.Equals(gp.Id, live.Id, StringComparison.OrdinalIgnoreCase))
                    return gp;
                if (GameDeduplicationEngine.NormalizeGameTitle(gp.Name) == key)
                    return gp;
            }
            return null;
        }
    }
}
