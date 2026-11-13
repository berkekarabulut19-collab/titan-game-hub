using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace TitanGameHub
{
    /// <summary>
    /// Arka plan güncelleme ön-indirmesi (Steam davranışı):
    /// Oyun çalışmıyorken, güncellemesi olan KURULU oyunların yeni paketini indirip
    /// Games klasörüne "<oyun>_pkg.rar|zip" olarak bırakır.
    /// Kullanıcı "Oyna" dediğinde indirme yapılmaz, doğrudan kurulur.
    /// </summary>
    public static class BackgroundUpdateService
    {
        private static Timer _timer;
        private static List<GameProfile> _profiles;
        private static Func<bool> _isBusy;
        private static Action<string> _status;
        private static volatile bool _running;
        private static DateTime _lastRun = DateTime.MinValue;

        public static bool IsEnabled { get; private set; }

        public static void Start(List<GameProfile> profiles, Func<bool> isGameOrDownloadBusy, Action<string> statusCallback, int intervalMinutes)
        {
            _profiles = profiles;
            _isBusy = isGameOrDownloadBusy;
            _status = statusCallback;
            IsEnabled = true;

            if (_timer != null) { _timer.Dispose(); _timer = null; }
            int interval = Math.Max(15, intervalMinutes);
            _timer = new Timer(delegate { Tick(); }, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(interval));
        }

        public static void Stop()
        {
            IsEnabled = false;
            if (_timer != null) { _timer.Dispose(); _timer = null; }
        }

        private static void Tick()
        {
            if (!IsEnabled || _running) return;
            if (DateTime.UtcNow - _lastRun < TimeSpan.FromMinutes(10)) return;

            try
            {
                if (_isBusy != null && _isBusy()) return;   // oyun/indirme sürüyorsa dokunma
            }
            catch { }

            _running = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    _lastRun = DateTime.UtcNow;
                    if (_profiles == null) return;

                    int prepared = 0;
                    foreach (GameProfile gp in new List<GameProfile>(_profiles))
                    {
                        if (gp == null || !gp.IsInstalled) continue;
                        if (gp.IsSteamManaged) continue;
                        if (!gp.UpdateAvailable) continue;
                        if (HasPreparedPackage(gp)) continue;

                        if (string.IsNullOrEmpty(gp.DirectMirrorUrl)) continue;
                        if (!gp.DirectMirrorUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                        if (DownloadHostPolicy.NeedsBrowserSession(gp.DirectMirrorUrl)) continue; // 403 host: tarayıcı gerekir

                        if (_isBusy != null && _isBusy()) break;

                        string dest;
                        if (PreDownload(gp, gp.DirectMirrorUrl, out dest))
                        {
                            prepared++;
                            if (_status != null)
                                _status(string.Format("⬇ '{0}' güncellemesi arka planda indirildi — Oyna dediğinizde anında kurulacak.", gp.Name));
                        }
                    }

                    if (prepared == 0 && _status != null)
                        _status("Arka plan: hazırlanacak güncelleme yok.");
                }
                catch { }
                finally { _running = false; }
            });
        }

        /// <summary>Bu oyun için hazır paket var mı?</summary>
        public static bool HasPreparedPackage(GameProfile gp)
        {
            try
            {
                string root = DownloadInstallService.GetEffectiveGamesRoot();
                string safe = Regex.Replace(gp.Name ?? "game", @"[^\w\s-]", "").Trim();
                foreach (string ext in new string[] { ".rar", ".zip", ".7z" })
                {
                    string p = Path.Combine(root, safe + "_pkg" + ext);
                    if (File.Exists(p) && new FileInfo(p).Length > 1024 * 1024) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool PreDownload(GameProfile gp, string url, out string destPath)
        {
            destPath = null;
            try
            {
                url = DownloadHostPolicy.NormalizeDirectUrl(url);
                string root = DownloadInstallService.GetEffectiveGamesRoot();
                if (!Directory.Exists(root)) Directory.CreateDirectory(root);

                string safe = Regex.Replace(gp.Name ?? "game", @"[^\w\s-]", "").Trim();
                string ext = url.IndexOf(".zip", StringComparison.OrdinalIgnoreCase) >= 0 ? ".zip" : ".rar";
                destPath = Path.Combine(root, safe + "_pkg" + ext);
                string temp = destPath + ".dl";

                long existing = File.Exists(temp) ? new FileInfo(temp).Length : 0;

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Proxy = null;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                req.Timeout = 20000;
                req.ReadWriteTimeout = 45000;
                if (existing > 0) req.AddRange(existing);

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    string ct = (resp.ContentType ?? "").ToLowerInvariant();
                    if (ct.Contains("text/html")) return false;

                    FileMode mode = (existing > 0 && resp.StatusCode == HttpStatusCode.PartialContent) ? FileMode.Append : FileMode.Create;
                    if (mode == FileMode.Create) existing = 0;

                    long total = existing + (resp.ContentLength > 0 ? resp.ContentLength : 0);
                    using (Stream rs = resp.GetResponseStream())
                    using (FileStream fs = new FileStream(temp, mode, FileAccess.Write, FileShare.Read))
                    {
                        byte[] buf = new byte[1048576];
                        long got = existing;
                        int read;
                        while ((read = rs.Read(buf, 0, buf.Length)) > 0)
                        {
                            if (_isBusy != null && _isBusy()) { fs.Close(); return false; }
                            fs.Write(buf, 0, read);
                            got += read;

                            if (total > 0 && got >= total) break;
                            if (_status != null && got % (256L * 1048576) < 1048576)
                                _status(string.Format("⬇ Arka plan indirme: {0} — {1:0.00} GB / {2:0.00} GB", gp.Name, got / 1073741824.0, total / 1073741824.0));
                        }
                    }
                }

                if (!File.Exists(temp)) return false;

                string magic;
                if (!DownloadInstallService.VerifyArchiveMagicBytes(temp, out magic)) return false;

                if (File.Exists(destPath)) File.Delete(destPath);
                File.Move(temp, destPath);
                return true;
            }
            catch { return false; }
        }
    }
}
