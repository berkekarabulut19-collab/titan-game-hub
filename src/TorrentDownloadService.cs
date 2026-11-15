using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TitanGameHub
{
    /// <summary>
    /// Torrent + doğrudan indirme motoru (aria2c JSON-RPC).
    ///
    /// Neden aria2: torrent ve HTTP'yi aynı motorda destekler, GUI/hesap gerektirmez,
    /// RPC ile tam kontrol verir. Seed KAPATILIR (seed-time=0) ve indirme bitince iş
    /// anında kaldırılır — telif teşhiri minimumda kalır.
    ///
    /// aria2c yolu: <uygulama>\aria2c.exe, D:\Apps\aria2\aria2c.exe veya ARIA2_PATH
    /// </summary>
    public static class TorrentDownloadService
    {
        private const int DefaultRpcPort = 16800;
        private const string RpcUrl = "http://127.0.0.1:16800/jsonrpc";
        private const string RpcSecret = "titan";
        private static string _activeRpcUrl = null;

        public class Config
        {
            public string Url = RpcUrl;
            public string Secret = RpcSecret;
        }

        public class TorrentProgress
        {
            public bool Completed;
            public double Percent;
            public long Downloaded;
            public long Size;
            public double SpeedBytesPerSec;
            public string State = "";
            public string ContentPath;
            public string Error;
        }

        public static Config LoadConfig()
        {
            Config cfg = new Config();
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "titan_user.cfg");
                if (!File.Exists(path)) return cfg;

                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("#") || !line.Contains("=")) continue;
                    string[] p = line.Split(new char[] { '=' }, 2);
                    if (p.Length < 2) continue;
                    string k = p[0].Trim().ToUpperInvariant();
                    string v = p[1].Trim();
                    if (k == "ARIA2_RPC_URL" && v.Length > 0) cfg.Url = v;
                    else if (k == "ARIA2_SECRET") cfg.Secret = v;
                }
            }
            catch { }
            return cfg;
        }

        public static string FindAria2()
        {
            try
            {
                string env = Environment.GetEnvironmentVariable("ARIA2_PATH");
                if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

                string[] candidates = new string[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aria2c.exe"),
                    @"D:\Apps\aria2\aria2c.exe",
                    @"C:\Program Files\aria2\aria2c.exe"
                };
                foreach (string c in candidates)
                    if (File.Exists(c)) return c;
            }
            catch { }
            return null;
        }

        public static bool IsAvailable(out string message)
        {
            message = "";
            try
            {
                if (RpcCall("aria2.getVersion", "") != null)
                {
                    message = "aria2 indirme motoru hazır";
                    return true;
                }
            }
            catch { }

            string exe = FindAria2();
            if (string.IsNullOrEmpty(exe))
            {
                message = "aria2c bulunamadı (D:\\Apps\\aria2\\aria2c.exe).";
                return false;
            }

            if (StartEngine(exe, null))
            {
                message = "aria2 indirme motoru başlatıldı";
                return true;
            }

            message = "aria2 başlatılamadı.";
            return false;
        }

        /// <summary>Aktif aria2 RPC motorunu temiz şekilde kapatır.</summary>
        public static void ShutdownEngine()
        {
            try { RpcCall("aria2.forceShutdown", ""); } catch { }
        }

        private static int FindAvailableRpcPort(int preferredPort)
        {
            if (IsPortFree(preferredPort)) return preferredPort;
            try
            {
                TcpListener l = new TcpListener(IPAddress.Loopback, 0);
                l.Start();
                int port = ((IPEndPoint)l.LocalEndpoint).Port;
                l.Stop();
                return port;
            }
            catch { return preferredPort; }
        }

        private static bool IsPortFree(int port)
        {
            TcpListener l = null;
            try
            {
                l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                return true;
            }
            catch { return false; }
            finally
            {
                if (l != null) { try { l.Stop(); } catch { } }
            }
        }

        /// <summary>aria2c'yi RPC modunda başlatır (yoksa).</summary>
        public static bool StartEngine(string exePath, string defaultDir)
        {
            try
            {
                if (RpcCall("aria2.getVersion", "") != null) return true;

                Config cfg = LoadConfig();
                int preferredPort = DefaultRpcPort;
                try
                {
                    Uri u = new Uri(cfg.Url);
                    if (u.Port > 0) preferredPort = u.Port;
                }
                catch { }

                int port = FindAvailableRpcPort(preferredPort);
                _activeRpcUrl = "http://127.0.0.1:" + port + "/jsonrpc";

                string dir = string.IsNullOrEmpty(defaultDir)
                    ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Games", "_incoming")
                    : defaultDir;
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--enable-rpc=true --rpc-listen-all=false --rpc-listen-port=" + port +
                                " --rpc-secret=" + cfg.Secret +
                                " --seed-time=0 --bt-stop-timeout=0" +
                                " --max-connection-per-server=16 --split=16 --min-split-size=1M" +
                                " --bt-max-peers=200 --bt-request-peer-speed-limit=0 --max-overall-download-limit=0" +
                                " --disk-cache=128M --file-allocation=trunc" +
                                " --continue=true --auto-file-renaming=false" +
                                " --dir=\"" + dir + "\"" +
                                " --console-log-level=error --summary-interval=0",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process proc = Process.Start(psi);

                for (int i = 0; i < 20; i++)
                {
                    Thread.Sleep(250);
                    if (proc != null && proc.HasExited) break;
                    if (RpcCall("aria2.getVersion", "") != null) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>Sayfadan magnet veya .torrent linki çıkarır.</summary>
        public static string ExtractMagnetOrTorrent(string html, string pageUrl)
        {
            if (string.IsNullOrEmpty(html)) return null;

            Match mag = Regex.Match(html, @"magnet:\?xt=urn:btih:[^""'\s<>]+", RegexOptions.IgnoreCase);
            if (mag.Success) return WebUtility.HtmlDecode(mag.Value);

            Match tor = Regex.Match(html, @"href=[""']([^""']+\.torrent)[""']", RegexOptions.IgnoreCase);
            if (tor.Success)
            {
                string link = WebUtility.HtmlDecode(tor.Groups[1].Value);
                if (link.StartsWith("//")) link = "https:" + link;
                else if (link.StartsWith("/") && !string.IsNullOrEmpty(pageUrl))
                {
                    try { link = new Uri(new Uri(pageUrl), link).ToString(); } catch { }
                }
                return link;
            }
            return null;
        }

        /// <summary>Torrent/magnet/URL indirir; bitince işi kaldırır (seed yok).</summary>
        public static TorrentProgress Download(string magnetOrUrl, string savePath, Action<TorrentProgress> onProgress, Func<bool> isCancelled, int timeoutMinutes)
        {
            TorrentProgress tp = new TorrentProgress();

            string exe = FindAria2();
            if (string.IsNullOrEmpty(exe))
            {
                tp.Error = "aria2c bulunamadı.";
                return tp;
            }
            if (!StartEngine(exe, savePath))
            {
                tp.Error = "aria2 motoru başlatılamadı.";
                return tp;
            }

            try
            {
                if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);

                string gid = AddDownload(magnetOrUrl, savePath);
                if (string.IsNullOrEmpty(gid))
                {
                    tp.Error = "İndirme eklenemedi (magnet/URL geçersiz).";
                    return tp;
                }

                DateTime deadline = DateTime.UtcNow.AddMinutes(Math.Max(10, timeoutMinutes));

                while (DateTime.UtcNow < deadline)
                {
                    if (isCancelled != null && isCancelled())
                    {
                        RpcCall("aria2.forceRemove", "\"" + gid + "\"");
                        tp.Error = "İptal edildi";
                        return tp;
                    }

                    Thread.Sleep(1500);

                    string json = RpcCall("aria2.tellStatus", "\"" + gid + "\",[\"status\",\"totalLength\",\"completedLength\",\"downloadSpeed\",\"files\",\"errorMessage\"]");
                    if (string.IsNullOrEmpty(json)) continue;

                    tp.State = JsonString(json, "status");
                    tp.Size = JsonLong(json, "totalLength");
                    tp.Downloaded = JsonLong(json, "completedLength");
                    tp.SpeedBytesPerSec = JsonDouble(json, "downloadSpeed");
                    tp.Percent = tp.Size > 0 ? Math.Round(tp.Downloaded * 100.0 / tp.Size, 1) : 0;

                    Match fm = Regex.Match(json, "\"path\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                    if (fm.Success) tp.ContentPath = Regex.Unescape(fm.Groups[1].Value);

                    if (onProgress != null) onProgress(tp);

                    if (tp.State == "complete")
                    {
                        tp.Completed = true;
                        tp.Percent = 100;
                        RpcCall("aria2.forceRemove", "\"" + gid + "\"");   // seeding yok
                        return tp;
                    }

                    if (tp.State == "error" || tp.State == "removed")
                    {
                        tp.Error = JsonString(json, "errorMessage");
                        if (string.IsNullOrEmpty(tp.Error)) tp.Error = "İndirme hatası (" + tp.State + ")";
                        return tp;
                    }
                }

                tp.Error = "Zaman aşımı (" + timeoutMinutes + " dk)";
                RpcCall("aria2.forceRemove", "\"" + gid + "\"");
                return tp;
            }
            catch (Exception ex)
            {
                tp.Error = "Torrent hatası: " + ex.Message;
                return tp;
            }
        }

        private static string AddDownload(string magnetOrUrl, string savePath)
        {
            try
            {
                if (magnetOrUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    string res = RpcCall("aria2.addUri",
                        "[\"" + Escape(magnetOrUrl) + "\"],{\"dir\":\"" + Escape(savePath) + "\",\"seed-time\":\"0\"}");
                    return JsonString(res, "result");
                }

                string res2 = RpcCall("aria2.addUri",
                    "[\"" + Escape(magnetOrUrl) + "\"],{\"dir\":\"" + Escape(savePath) + "\",\"continue\":\"true\",\"auto-file-renaming\":\"false\"}");
                return JsonString(res2, "result");
            }
            catch { return null; }
        }

        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>paramsElements: token'dan SONRA gelen JSON elemanları (köşeli parantezsiz).</summary>
        private static string RpcCall(string method, string paramsElements)
        {
            try
            {
                Config cfg = LoadConfig();
                string targetUrl = !string.IsNullOrEmpty(_activeRpcUrl) ? _activeRpcUrl : cfg.Url;
                string body = "{\"jsonrpc\":\"2.0\",\"id\":\"titan\",\"method\":\"" + method +
                              "\",\"params\":[\"token:" + cfg.Secret + "\"" +
                              (string.IsNullOrEmpty(paramsElements) ? "" : "," + paramsElements) + "]}";

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(targetUrl);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Proxy = null;
                req.Timeout = 8000;
                byte[] data = Encoding.UTF8.GetBytes(body);
                req.ContentLength = data.Length;
                using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                    return sr.ReadToEnd();
            }
            catch (Exception ex)
            {
                try
                {
                    string d = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                    if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                    File.AppendAllText(Path.Combine(d, "rpc_debug.txt"),
                        DateTime.Now.ToString("HH:mm:ss") + " " + method + " HATA: " + ex.Message + Environment.NewLine);
                }
                catch { }
                return null;
            }
        }

        private static string JsonString(string json, string field)
        {
            if (string.IsNullOrEmpty(json)) return "";
            Match m = Regex.Match(json, "\"" + field + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : "";
        }

        private static long JsonLong(string json, string field)
        {
            double d = JsonDouble(json, field);
            return (long)d;
        }

        private static double JsonDouble(string json, string field)
        {
            if (string.IsNullOrEmpty(json)) return 0;
            Match m = Regex.Match(json, "\"" + field + "\"\\s*:\\s*\"?(-?[0-9\\.]+)\"?");
            double v;
            if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v))
                return v;
            return 0;
        }

        /// <summary>İndirilen içerikten kurulabilir arşivi bulur (part1 öncelikli).</summary>
        public static string FindArchiveInContent(string contentPath)
        {
            try
            {
                if (string.IsNullOrEmpty(contentPath)) return null;

                if (File.Exists(contentPath))
                {
                    string ext = Path.GetExtension(contentPath).ToLowerInvariant();
                    if (ext == ".rar" || ext == ".zip" || ext == ".7z") return contentPath;
                    return null;
                }

                if (!Directory.Exists(contentPath)) return null;

                string best = null;
                long bestSize = 0;
                foreach (string f in Directory.GetFiles(contentPath, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".rar" && ext != ".zip" && ext != ".7z") continue;

                    bool isPart = Regex.IsMatch(Path.GetFileName(f), @"(?i)part(0*)(2|3|4|5|6|7|8|9)|\.\d{3}$");
                    if (isPart) continue;

                    long size = new FileInfo(f).Length;
                    if (size > bestSize) { bestSize = size; best = f; }
                }
                return best;
            }
            catch { return null; }
        }

        /// <summary>
        /// qBittorrent WebUI yapılandırma kontrolü (titan_user.cfg: QBIT_URL).
        /// </summary>
        public static bool IsQBitConfigured()
        {
            try
            {
                string userCfg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "titan_user.cfg");
                if (!File.Exists(userCfg)) return false;
                foreach (string raw in File.ReadAllLines(userCfg))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("#") || !line.Contains("=")) continue;
                    string[] p = line.Split(new char[] { '=' }, 2);
                    if (p.Length < 2) continue;
                    if (p[0].Trim().ToUpperInvariant() == "QBIT_URL" && !string.IsNullOrEmpty(p[1].Trim()))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static string GetQBitConfigValue(string keyUpper)
        {
            try
            {
                string userCfg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "titan_user.cfg");
                if (!File.Exists(userCfg)) return null;
                foreach (string raw in File.ReadAllLines(userCfg))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("#") || !line.Contains("=")) continue;
                    string[] p = line.Split(new char[] { '=' }, 2);
                    if (p.Length < 2) continue;
                    if (p[0].Trim().ToUpperInvariant() == keyUpper) return p[1].Trim();
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// qBittorrent WebUI v2 API: oturum aç ve torrent ekle.
        /// </summary>
        public static bool AddToQBit(string magnetOrUrl, string savePath, out string message)
        {
            message = "";
            try
            {
                string baseUrl = GetQBitConfigValue("QBIT_URL");
                if (string.IsNullOrEmpty(baseUrl))
                {
                    message = "QBIT_URL tanımlı değil (bin\titan_user.cfg).";
                    return false;
                }
                baseUrl = baseUrl.TrimEnd('/');
                string user = GetQBitConfigValue("QBIT_USER") ?? "admin";
                string pass = GetQBitConfigValue("QBIT_PASS") ?? "";

                CookieContainer cookies = new CookieContainer();

                // Login
                HttpWebRequest loginReq = (HttpWebRequest)WebRequest.Create(baseUrl + "/api/v2/auth/login");
                loginReq.Method = "POST";
                loginReq.ContentType = "application/x-www-form-urlencoded";
                loginReq.CookieContainer = cookies;
                loginReq.Timeout = 8000;
                byte[] loginBody = Encoding.UTF8.GetBytes("username=" + Uri.EscapeDataString(user) + "&password=" + Uri.EscapeDataString(pass));
                loginReq.ContentLength = loginBody.Length;
                using (Stream s = loginReq.GetRequestStream()) s.Write(loginBody, 0, loginBody.Length);
                using (HttpWebResponse loginResp = (HttpWebResponse)loginReq.GetResponse())
                using (StreamReader sr = new StreamReader(loginResp.GetResponseStream()))
                {
                    string resp = sr.ReadToEnd();
                    if (resp.IndexOf("Fails", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        message = "qBittorrent oturum açma başarısız (kullanıcı/şifre).";
                        return false;
                    }
                }

                // Add torrent (multipart)
                string boundary = "----TitanBoundary" + DateTime.UtcNow.Ticks.ToString("x");
                HttpWebRequest addReq = (HttpWebRequest)WebRequest.Create(baseUrl + "/api/v2/torrents/add");
                addReq.Method = "POST";
                addReq.ContentType = "multipart/form-data; boundary=" + boundary;
                addReq.CookieContainer = cookies;
                addReq.Timeout = 15000;

                StringBuilder sb = new StringBuilder();
                sb.Append("--").Append(boundary).Append("\r\n");
                sb.Append("Content-Disposition: form-data; name=\"urls\"\r\n\r\n");
                sb.Append(magnetOrUrl).Append("\r\n");
                if (!string.IsNullOrEmpty(savePath))
                {
                    sb.Append("--").Append(boundary).Append("\r\n");
                    sb.Append("Content-Disposition: form-data; name=\"savepath\"\r\n\r\n");
                    sb.Append(savePath).Append("\r\n");
                }
                sb.Append("--").Append(boundary).Append("\r\n");
                sb.Append("Content-Disposition: form-data; name=\"paused\"\r\n\r\nfalse\r\n");
                sb.Append("--").Append(boundary).Append("--\r\n");

                byte[] addBody = Encoding.UTF8.GetBytes(sb.ToString());
                addReq.ContentLength = addBody.Length;
                using (Stream s = addReq.GetRequestStream()) s.Write(addBody, 0, addBody.Length);
                using (HttpWebResponse addResp = (HttpWebResponse)addReq.GetResponse())
                using (StreamReader sr = new StreamReader(addResp.GetResponseStream()))
                {
                    string resp = sr.ReadToEnd();
                    if (resp.IndexOf("Ok", StringComparison.OrdinalIgnoreCase) >= 0 || addResp.StatusCode == HttpStatusCode.OK)
                    {
                        string shortUrl = magnetOrUrl.Length > 60 ? magnetOrUrl.Substring(0, 60) + "..." : magnetOrUrl;
                        message = "qBittorrent'e eklendi: " + shortUrl;
                        return true;
                    }
                    message = "qBittorrent yanıtı: " + resp;
                    return false;
                }
            }
            catch (Exception ex)
            {
                message = "qBittorrent API hatası: " + ex.Message;
                return false;
            }
        }

    }
}
