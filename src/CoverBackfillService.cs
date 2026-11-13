using System;
using System.Collections.Generic;
using System.Threading;

namespace TitanGameHub
{
    /// <summary>
    /// Kataloğta kapağı (afişi) olmayan oyunlar için toplu görsel tamamlama.
    /// Steam mağaza araması üzerinden resmi header/kapsül görseli çeker.
    /// Hem arayüzdeki buton hem "--fill-covers" komutu bunu kullanır.
    /// </summary>
    public static class CoverBackfillService
    {
        public class Result
        {
            public int Scanned;
            public int Missing;
            public int Filled;
            public List<string> Unresolved = new List<string>();
        }

        public static Result Run(List<GameProfile> catalog, int limit, Action<string, int> progress)
        {
            Result res = new Result();
            if (catalog == null) return res;

            List<GameProfile> targets = new List<GameProfile>();
            foreach (GameProfile gp in catalog)
            {
                if (gp == null) continue;
                res.Scanned++;
                if (string.IsNullOrEmpty(gp.BannerUrl)) targets.Add(gp);
            }

            res.Missing = targets.Count;
            if (targets.Count == 0) return res;

            int max = limit > 0 ? Math.Min(limit, targets.Count) : targets.Count;

            for (int i = 0; i < max; i++)
            {
                GameProfile gp = targets[i];
                if (progress != null)
                    progress(string.Format("Kapak aranıyor: {0} ({1}/{2})", gp.Name, i + 1, max),
                             (int)((i * 100.0) / Math.Max(1, max)));

                try
                {
                    string art = null;
                    ManualResetEvent ev = new ManualResetEvent(false);

                    string cached = SteamArtworkResolver.GetCached(gp.Name);
                    if (!string.IsNullOrEmpty(cached)) { art = cached; }
                    else
                    {
                        // force: önceki negatif sonuç önbellekte olsa bile yeniden dene
                        SteamArtworkResolver.ResolveAsync(gp.Name, delegate(string url) { art = url; ev.Set(); }, true);
                        ev.WaitOne(15000);
                    }

                    if (!string.IsNullOrEmpty(art))
                    {
                        gp.BannerUrl = art;
                        gp.CapsuleUrl = art;
                        res.Filled++;
                    }
                    else if (res.Unresolved.Count < 40)
                    {
                        res.Unresolved.Add(gp.Name);
                    }
                }
                catch { }

                Thread.Sleep(120);
            }

            if (progress != null) progress("Kapak taraması bitti.", 100);
            return res;
        }
    }
}
