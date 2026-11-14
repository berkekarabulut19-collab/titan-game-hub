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
            int doneCount = 0;
            int filledCount = 0;
            object sync = new object();
            int workers = Math.Min(8, max);
            int nextIndex = -1;
            ManualResetEvent allDone = new ManualResetEvent(false);

            for (int w = 0; w < workers; w++)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    while (true)
                    {
                        int idx = Interlocked.Increment(ref nextIndex);
                        if (idx >= max) break;

                        GameProfile gp = targets[idx];
                        try
                        {
                            string art = SteamArtworkResolver.GetCached(gp.Name);
                            if (string.IsNullOrEmpty(art))
                            {
                                ManualResetEvent ev = new ManualResetEvent(false);
                                SteamArtworkResolver.ResolveAsync(gp.Name, delegate(string url) { art = url; ev.Set(); }, true);
                                ev.WaitOne(8000);
                            }

                            if (!string.IsNullOrEmpty(art))
                            {
                                gp.BannerUrl = art;
                                gp.CapsuleUrl = art;
                                Interlocked.Increment(ref filledCount);
                            }
                            else
                            {
                                lock (sync)
                                {
                                    if (res.Unresolved.Count < 40) res.Unresolved.Add(gp.Name);
                                }
                            }
                        }
                        catch { }

                        int d = Interlocked.Increment(ref doneCount);
                        if (progress != null)
                        {
                            progress(string.Format("Kapak aranıyor: {0} ({1}/{2})", gp.Name, d, max),
                                     (int)((d * 100.0) / Math.Max(1, max)));
                        }
                        if (d >= max) allDone.Set();
                    }
                });
            }

            if (max > 0) allDone.WaitOne(120000);
            res.Filled = filledCount;

            if (progress != null) progress("Kapak taraması bitti.", 100);
            return res;
        }
    }
}
