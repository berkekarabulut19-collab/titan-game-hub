using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace TitanGameHub
{
    /// <summary>
    /// Büyük oyunlar (50–150 GB) tek dosya olarak gelmiyor: FitGirl/OvaGames/SteamRIP
    /// çok parçalı RAR (part1.rar, part2.rar ... / .001, .002 ...) veriyor.
    ///
    /// Bu sınıf:
    ///  - ilk parçanın URL'sinden kardeş parçaları üretir (part1→part2→part3 ... veya .001→.002)
    ///  - her parçayı HTTP Range ile, kaldığı yerden devam ederek indirir
    ///  - toplam ilerlemeyi tek yüzde olarak bildirir
    ///  - iptalde kısmi parçaları silmez (sonraki denemede devam eder)
    /// </summary>
    public static class MultiPartDownloader
    {
        public class PartDownloadResult
        {
            public bool Success;
            public List<string> PartFiles = new List<string>();
            public string FirstPartPath;
            public long TotalBytes;
            public string Message;
        }

        private static readonly string[] PartTokenPatterns = new string[]
        {
            @"(?i)(part)(0*)(\d+)(\.rar|\.zip|\.7z|$)",
            @"(?i)()(\.)(\d{3})$"
        };

        /// <summary>URL çok parçalı arşiv kalıbına uyuyor mu?</summary>
        public static bool IsPartUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            foreach (string p in PartTokenPatterns)
                if (Regex.IsMatch(url, p)) return true;
            return false;
        }

        /// <summary>part1 → part2/part3 ... (veya .001 → .002) adaylarını üretir.</summary>
        public static List<string> BuildPartCandidates(string firstUrl, int maxParts)
        {
            List<string> list = new List<string>();
            list.Add(firstUrl);
            if (!IsPartUrl(firstUrl)) return list;

            for (int i = 2; i <= maxParts; i++)
            {
                string candidate = null;

                Match m1 = Regex.Match(firstUrl, @"(?i)(part)(0*)(\d+)(\.rar|\.zip|\.7z|$)");
                if (m1.Success)
                {
                    int width = m1.Groups[2].Value.Length + m1.Groups[3].Value.Length;
                    string digits = i.ToString().PadLeft(width, '0');
                    candidate = firstUrl.Substring(0, m1.Index) + m1.Groups[1].Value + digits + m1.Groups[4].Value +
                                firstUrl.Substring(m1.Index + m1.Length);
                }
                else
                {
                    Match m2 = Regex.Match(firstUrl, @"()(\.)(\d{3})$");
                    if (m2.Success)
                        candidate = firstUrl.Substring(0, m2.Index) + "." + i.ToString("000");
                }

                if (string.IsNullOrEmpty(candidate) || candidate == firstUrl) break;
                list.Add(candidate);
            }
            return list;
        }

        private static bool RemoteExists(string url)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "HEAD";
                    req.Proxy = null;
                    req.Timeout = 12000;
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    {
                        long len = resp.ContentLength;
                        return resp.StatusCode == HttpStatusCode.OK && len != 0;
                    }
                }
                catch (WebException wex)
                {
                    HttpWebResponse r = wex.Response as HttpWebResponse;
                    if (r != null && (r.StatusCode == HttpStatusCode.Forbidden || r.StatusCode == HttpStatusCode.Unauthorized))
                        return true; // 403: var ama bot korumalı
                    Thread.Sleep(400);
                }
                catch { Thread.Sleep(400); }
            }
            return false;
        }

        /// <summary>
        /// Parçaları sırayla indirir. destinationFolder içine "part1..N" olarak yazar.
        /// progress(parçaIndex, parçaSayısı, indirilenByte, toplamByte, hızMB)
        /// </summary>
        public static PartDownloadResult Download(
            string firstUrl,
            string destinationFolder,
            string fileBaseName,
            Func<bool> isCancelled,
            Action<int, int, long, long, double, string> progress)
        {
            PartDownloadResult result = new PartDownloadResult();

            try
            {
                if (!Directory.Exists(destinationFolder)) Directory.CreateDirectory(destinationFolder);

                string extension = ".rar";
                Match extMatch = Regex.Match(firstUrl, @"(?i)\.(rar|zip|7z)");
                if (extMatch.Success) extension = "." + extMatch.Groups[1].Value.ToLowerInvariant();

                // Kaç parça var? (1..99 arası HEAD ile doğrula)
                List<string> urls = BuildPartCandidates(firstUrl, 199);
                List<string> confirmed = new List<string>();
                confirmed.Add(urls[0]);
                for (int i = 1; i < urls.Count; i++)
                {
                    if (isCancelled != null && isCancelled()) break;
                    if (!RemoteExists(urls[i])) break;
                    confirmed.Add(urls[i]);
                }

                result.Message = string.Format("{0} parça bulundu", confirmed.Count);

                long totalWritten = 0;
                for (int i = 0; i < confirmed.Count; i++)
                {
                    if (isCancelled != null && isCancelled())
                    {
                        result.Message = "İptal edildi (indirilen parçalar korundu)";
                        return result;
                    }

                    string partPath = Path.Combine(destinationFolder,
                        string.Format("{0}.part{1:000}{2}", fileBaseName, i + 1, extension));

                    bool ok = DownloadSingle(confirmed[i], partPath, i, confirmed.Count, isCancelled, progress, totalWritten);
                    if (!ok)
                    {
                        result.Message = string.Format("Parça {0}/{1} indirilemedi", i + 1, confirmed.Count);
                        return result;
                    }

                    long size = new FileInfo(partPath).Length;
                    totalWritten += size;
                    result.PartFiles.Add(partPath);
                }

                result.FirstPartPath = result.PartFiles.Count > 0 ? result.PartFiles[0] : null;
                result.TotalBytes = totalWritten;
                result.Success = result.PartFiles.Count > 0;
                result.Message = string.Format("{0} parça / {1:0.0} GB indirildi", result.PartFiles.Count, totalWritten / 1073741824.0);
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = "Çok parçalı indirme hatası: " + ex.Message;
                return result;
            }
        }

        private static bool DownloadSingle(
            string url, string destPath, int partIndex, int partCount,
            Func<bool> isCancelled,
            Action<int, int, long, long, double, string> progress,
            long alreadyDoneBytes)
        {
            for (int attempt = 1; attempt <= 4; attempt++)
            {
                if (isCancelled != null && isCancelled()) return false;

                long existing = File.Exists(destPath) ? new FileInfo(destPath).Length : 0;

                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Proxy = null;
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                    req.Timeout = 25000;
                    req.ReadWriteTimeout = 60000;
                    req.ServicePoint.UseNagleAlgorithm = false;
                    req.ServicePoint.Expect100Continue = false;
                    req.ServicePoint.ConnectionLimit = 128;
                    req.ServicePoint.ReceiveBufferSize = 8388608;
                    if (existing > 0) req.AddRange(existing);

                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    {
                        string ct = (resp.ContentType ?? "").ToLowerInvariant();
                        if (ct.Contains("text/html"))
                            return false;

                        long partTotal = existing + (resp.ContentLength > 0 ? resp.ContentLength : 0);
                        FileMode mode = (existing > 0 && resp.StatusCode == HttpStatusCode.PartialContent)
                            ? FileMode.Append : FileMode.Create;
                        if (mode == FileMode.Create) existing = 0;

                        using (Stream rs = resp.GetResponseStream())
                        using (FileStream fs = new FileStream(destPath, mode, FileAccess.Write, FileShare.Read, 1048576))
                        {
                            try { if (rs.CanTimeout) rs.ReadTimeout = 20000; } catch { }
                            byte[] buf = new byte[1048576];
                            long got = existing;
                            int read;
                            DateTime lastReport = DateTime.UtcNow;
                            long lastBytes = got;

                            while ((read = rs.Read(buf, 0, buf.Length)) > 0)
                            {
                                if (isCancelled != null && isCancelled()) return false;

                                fs.Write(buf, 0, read);
                                got += read;

                                if ((DateTime.UtcNow - lastReport).TotalMilliseconds >= 400)
                                {
                                    double secs = (DateTime.UtcNow - lastReport).TotalSeconds;
                                    double speed = ((got - lastBytes) / 1048576.0) / Math.Max(0.2, secs);
                                    lastReport = DateTime.UtcNow;
                                    lastBytes = got;

                                    if (progress != null)
                                    {
                                        string label = partCount > 1
                                            ? string.Format("Parça {0}/{1}", partIndex + 1, partCount)
                                            : "İndiriliyor";
                                        progress(partIndex, partCount, alreadyDoneBytes + got, alreadyDoneBytes + partTotal, speed, label);
                                    }
                                }
                            }

                            if (progress != null)
                            {
                                string doneLabel = partCount > 1
                                    ? string.Format("Parça {0}/{1} tamamlandı", partIndex + 1, partCount)
                                    : "İndirme tamamlandı";
                                progress(partIndex, partCount, alreadyDoneBytes + got, Math.Max(alreadyDoneBytes + got, alreadyDoneBytes + partTotal), 0, doneLabel);
                            }
                        }
                    }

                    if (!File.Exists(destPath) || new FileInfo(destPath).Length == 0)
                        throw new IOException("Boş yanıt");

                    return true;
                }
                catch
                {
                    if (isCancelled != null && isCancelled()) return false;
                    Thread.Sleep(1500 * attempt);
                }
            }
            return false;
        }
    }
}
