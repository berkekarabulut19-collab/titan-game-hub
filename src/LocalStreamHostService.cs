using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TitanGameHub
{
    public static class LocalStreamHostService
    {
        private static HttpListener _listener;
        private static Thread _serverThread;
        private static bool _isRunning = false;
        private static int _port = 48085;
        private static readonly ImageCodecInfo _jpegCodec;
        private static readonly EncoderParameters _encoderParams;

        static LocalStreamHostService()
        {
            foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
            {
                if (codec.FormatID == ImageFormat.Jpeg.Guid)
                {
                    _jpegCodec = codec;
                    break;
                }
            }
            _encoderParams = new EncoderParameters(1);
            _encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 65L);
        }

        public static bool IsRunning
        {
            get { return _isRunning; }
        }

        public static int Port
        {
            get { return _port; }
        }

        public static bool StartStream(int port, out string localUrl)
        {
            localUrl = "";
            if (_isRunning)
            {
                localUrl = GetLocalStreamUrl();
                return true;
            }

            try
            {
                _port = port > 0 ? port : 48085;
                _listener = new HttpListener();
                _listener.Prefixes.Add(string.Format("http://+:{0}/", _port));
                _listener.Start();

                _isRunning = true;
                _serverThread = new Thread(ServerWorkerLoop);
                _serverThread.IsBackground = true;
                _serverThread.Start();

                localUrl = GetLocalStreamUrl();
                return true;
            }
            catch
            {
                // Fallback to localhost prefix if wildcard requires elevation
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add(string.Format("http://localhost:{0}/", _port));
                    _listener.Prefixes.Add(string.Format("http://127.0.0.1:{0}/", _port));
                    _listener.Start();

                    _isRunning = true;
                    _serverThread = new Thread(ServerWorkerLoop);
                    _serverThread.IsBackground = true;
                    _serverThread.Start();

                    localUrl = string.Format("http://localhost:{0}/", _port);
                    return true;
                }
                catch (Exception ex)
                {
                    _isRunning = false;
                    localUrl = "Hata: " + ex.Message;
                    return false;
                }
            }
        }

        public static void StopStream()
        {
            _isRunning = false;
            if (_listener != null)
            {
                try { _listener.Stop(); } catch { }
                _listener = null;
            }
        }

        public static string GetLocalStreamUrl()
        {
            string ip = GetLocalIPAddress();
            return string.Format("http://{0}:{1}/", ip, _port);
        }

        private static string GetLocalIPAddress()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    if (endPoint != null) return endPoint.Address.ToString();
                }
            }
            catch { }
            return "127.0.0.1";
        }

        private static void ServerWorkerLoop()
        {
            while (_isRunning && _listener != null)
            {
                try
                {
                    HttpListenerContext context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        HandleClientRequest(context);
                    });
                }
                catch
                {
                    if (!_isRunning) break;
                }
            }
        }

        private static void HandleClientRequest(HttpListenerContext context)
        {
            try
            {
                string rawUrl = context.Request.RawUrl ?? "/";
                if (rawUrl.StartsWith("/stream.mjpeg", StringComparison.OrdinalIgnoreCase))
                {
                    ServeMjpegStream(context);
                }
                else if (rawUrl.StartsWith("/api/p2p_swarm/manifest", StringComparison.OrdinalIgnoreCase))
                {
                    ServeP2pManifest(context);
                }
                else if (rawUrl.StartsWith("/api/p2p_swarm/download", StringComparison.OrdinalIgnoreCase))
                {
                    ServeP2pDownload(context);
                }
                else if (rawUrl.StartsWith("/api/fleet/hub_version", StringComparison.OrdinalIgnoreCase))
                {
                    ServeHubVersion(context);
                }
                else if (rawUrl.StartsWith("/api/fleet/download_hub", StringComparison.OrdinalIgnoreCase))
                {
                    ServeHubBinary(context);
                }
                else if (rawUrl.StartsWith("/api/swarm/manifest", StringComparison.OrdinalIgnoreCase))
                {
                    ServeSwarmManifest(context);
                }
                else if (rawUrl.StartsWith("/api/swarm/chunk", StringComparison.OrdinalIgnoreCase))
                {
                    ServeSwarmChunk(context);
                }
                else
                {
                    ServeHtmlPlayer(context);
                }
            }
            catch { }
        }

        private static void ServeP2pManifest(HttpListenerContext context)
        {
            try
            {
                List<FleetSyncGameItem> games = FleetSyncService.GetAllSwarmGames();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[");
                for (int i = 0; i < games.Count; i++)
                {
                    FleetSyncGameItem g = games[i];
                    sb.AppendFormat("  {{\"id\":\"{0}\",\"name\":\"{1}\",\"version\":\"{2}\",\"size\":{3}}}",
                        g.GameId, g.GameName, g.Version, g.FileSizeBytes);
                    if (i < games.Count - 1) sb.AppendLine(",");
                    else sb.AppendLine();
                }
                sb.AppendLine("]");

                byte[] b = Encoding.UTF8.GetBytes(sb.ToString());
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = b.Length;
                context.Response.OutputStream.Write(b, 0, b.Length);
                context.Response.OutputStream.Close();
            }
            catch { }
        }

        private static void ServeP2pDownload(HttpListenerContext context)
        {
            try
            {
                string rawUrl = context.Request.RawUrl ?? "";
                Match m = Regex.Match(rawUrl, @"[?&]id=([a-zA-Z0-9_-]+)");
                if (!m.Success)
                {
                    context.Response.StatusCode = 400;
                    context.Response.OutputStream.Close();
                    return;
                }

                string gameId = m.Groups[1].Value;
                FleetSyncGameItem item;
                if (!FleetSyncService.TryGetSwarmGame(gameId, out item) || !File.Exists(item.ArchivePath))
                {
                    context.Response.StatusCode = 404;
                    context.Response.OutputStream.Close();
                    return;
                }

                FileInfo fi = new FileInfo(item.ArchivePath);
                long fileLength = fi.Length;
                long startPos = 0;
                long endPos = fileLength - 1;

                string rangeHeader = context.Request.Headers["Range"];
                bool isPartial = false;
                if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
                {
                    string rangeSpec = rangeHeader.Substring(6);
                    string[] parts = rangeSpec.Split('-');
                    if (long.TryParse(parts[0], out startPos))
                    {
                        if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
                        {
                            long pEnd;
                            if (long.TryParse(parts[1], out pEnd)) endPos = pEnd;
                        }
                        isPartial = true;
                    }
                }

                long lengthToSend = endPos - startPos + 1;
                context.Response.ContentType = "application/octet-stream";
                context.Response.AddHeader("Accept-Ranges", "bytes");

                if (isPartial)
                {
                    context.Response.StatusCode = 206; // Partial Content
                    context.Response.AddHeader("Content-Range", string.Format("bytes {0}-{1}/{2}", startPos, endPos, fileLength));
                }
                else
                {
                    context.Response.StatusCode = 200;
                }
                context.Response.ContentLength64 = lengthToSend;

                using (FileStream fs = new FileStream(item.ArchivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(startPos, SeekOrigin.Begin);
                    byte[] buffer = new byte[262144]; // 256 KB socket chunk buffer
                    long remaining = lengthToSend;
                    while (remaining > 0)
                    {
                        int toRead = (int)Math.Min(buffer.Length, remaining);
                        int read = fs.Read(buffer, 0, toRead);
                        if (read <= 0) break;

                        context.Response.OutputStream.Write(buffer, 0, read);
                        remaining -= read;
                    }
                }
                context.Response.OutputStream.Close();
            }
            catch { }
        }

        private static void ServeHubVersion(HttpListenerContext context)
        {
            try
            {
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                long exeTime = File.GetLastWriteTimeUtc(exePath).Ticks / TimeSpan.TicksPerSecond;
                string json = string.Format("{{\"version\":\"{0}\",\"timestamp\":{1}}}", AutoUpdateService.CurrentAppVersion, exeTime);
                byte[] b = Encoding.UTF8.GetBytes(json);
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = b.Length;
                context.Response.OutputStream.Write(b, 0, b.Length);
                context.Response.OutputStream.Close();
            }
            catch { }
        }

        private static void ServeHubBinary(HttpListenerContext context)
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string tempStage = Path.Combine(Path.GetTempPath(), "TitanDirectSync_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                string tempZip = tempStage + ".zip";
                Directory.CreateDirectory(tempStage);

                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                File.Copy(exePath, Path.Combine(tempStage, "TitanGameHub.exe"), true);

                string cfgPath = Path.Combine(appDir, "games_v6.cfg");
                if (File.Exists(cfgPath)) File.Copy(cfgPath, Path.Combine(tempStage, "games_v6.cfg"), true);

                string icoPath = Path.Combine(appDir, "app.ico");
                if (File.Exists(icoPath)) File.Copy(icoPath, Path.Combine(tempStage, "app.ico"), true);

                ZipFile.CreateFromDirectory(tempStage, tempZip);
                try { Directory.Delete(tempStage, true); } catch { }

                byte[] data = File.ReadAllBytes(tempZip);
                try { File.Delete(tempZip); } catch { }

                context.Response.ContentType = "application/zip";
                context.Response.AddHeader("Content-Disposition", "attachment; filename=\"TitanGameHub_Direct.zip\"");
                context.Response.ContentLength64 = data.Length;
                context.Response.OutputStream.Write(data, 0, data.Length);
                context.Response.OutputStream.Close();
            }
            catch { }
        }

        private static void ServeSwarmManifest(HttpListenerContext context)
        {
            try
            {
                string rawUrl = context.Request.RawUrl ?? "";
                Match m = Regex.Match(rawUrl, @"[?&]id=([a-zA-Z0-9_-]+)");
                if (!m.Success)
                {
                    context.Response.StatusCode = 400;
                    context.Response.OutputStream.Close();
                    return;
                }
                string gameId = m.Groups[1].Value;
                FleetSyncGameItem item;
                if (!FleetSyncService.TryGetSwarmGame(gameId, out item) || !File.Exists(item.ArchivePath))
                {
                    context.Response.StatusCode = 404;
                    context.Response.OutputStream.Close();
                    return;
                }

                SwarmManifest manifest = P2pSwarmChunkerService.CreateManifest(gameId, item.ArchivePath);
                string json = P2pSwarmChunkerService.SerializeManifestJson(manifest);
                byte[] b = Encoding.UTF8.GetBytes(json);
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = b.Length;
                context.Response.OutputStream.Write(b, 0, b.Length);
                context.Response.OutputStream.Close();
            }
            catch { }
        }

        private static void ServeSwarmChunk(HttpListenerContext context)
        {
            try
            {
                string rawUrl = context.Request.RawUrl ?? "";
                Match mId = Regex.Match(rawUrl, @"[?&]id=([a-zA-Z0-9_-]+)");
                Match mIdx = Regex.Match(rawUrl, @"[?&]index=([0-9]+)");
                if (!mId.Success || !mIdx.Success)
                {
                    context.Response.StatusCode = 400;
                    context.Response.OutputStream.Close();
                    return;
                }

                string gameId = mId.Groups[1].Value;
                int chunkIndex = int.Parse(mIdx.Groups[1].Value);

                FleetSyncGameItem item;
                if (!FleetSyncService.TryGetSwarmGame(gameId, out item) || !File.Exists(item.ArchivePath))
                {
                    context.Response.StatusCode = 404;
                    context.Response.OutputStream.Close();
                    return;
                }

                byte[] chunkData;
                string chunkHash;
                if (!P2pSwarmChunkerService.ReadChunk(item.ArchivePath, chunkIndex, out chunkData, out chunkHash))
                {
                    context.Response.StatusCode = 500;
                    context.Response.OutputStream.Close();
                    return;
                }

                context.Response.ContentType = "application/octet-stream";
                context.Response.AddHeader("X-Chunk-Sha256", chunkHash);
                context.Response.ContentLength64 = chunkData.Length;
                context.Response.OutputStream.Write(chunkData, 0, chunkData.Length);
                context.Response.OutputStream.Close();
            }
            catch { }
        }

        private static void ServeHtmlPlayer(HttpListenerContext context)
        {
            string html =
                "<!DOCTYPE html><html><head><meta charset='utf-8'/><meta name='viewport' content='width=device-width,initial-scale=1'/>" +
                "<title>Steam Remote Play // Titan Stream Hub</title>" +
                "<style>" +
                "body{margin:0;background:#0e141b;color:#c7d5e0;font-family:'Segoe UI',sans-serif;display:flex;flex-direction:column;align-items:center;justify-content:center;height:100vh;overflow:hidden;}" +
                ".header{position:fixed;top:0;left:0;right:0;background:rgba(20,29,41,0.9);padding:10px 20px;display:flex;justify-content:space-between;align-items:center;border-bottom:1px solid #2a3f5a;font-size:13px;font-weight:bold;color:#66c0f4;z-index:10;}" +
                "#screen{max-width:100vw;max-height:100vh;object-fit:contain;box-shadow:0 0 30px rgba(0,0,0,0.8);}" +
                "</style></head><body>" +
                "<div class='header'><span>🎮 STEAM REMOTE PLAY // TITAN</span><span>60 FPS P2P LAN STREAM</span></div>" +
                "<img id='screen' src='/stream.mjpeg' alt='Live Game Stream'/>" +
                "</body></html>";

            byte[] b = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = b.Length;
            context.Response.OutputStream.Write(b, 0, b.Length);
            context.Response.OutputStream.Close();
        }

        private static void ServeMjpegStream(HttpListenerContext context)
        {
            context.Response.ContentType = "multipart/x-mixed-replace; boundary=--titanframe";
            Stream output = context.Response.OutputStream;

            Rectangle bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            int targetWidth = Math.Min(1280, bounds.Width);
            int targetHeight = (int)((double)bounds.Height * targetWidth / bounds.Width);

            byte[] header = Encoding.ASCII.GetBytes("\r\n--titanframe\r\nContent-Type: image/jpeg\r\n\r\n");

            while (_isRunning && context.Response.OutputStream.CanWrite)
            {
                try
                {
                    using (Bitmap screenBmp = new Bitmap(bounds.Width, bounds.Height))
                    {
                        using (Graphics g = Graphics.FromImage(screenBmp))
                        {
                            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                        }

                        using (Bitmap resized = new Bitmap(targetWidth, targetHeight))
                        {
                            using (Graphics gr = Graphics.FromImage(resized))
                            {
                                gr.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                                gr.DrawImage(screenBmp, 0, 0, targetWidth, targetHeight);
                            }

                            using (MemoryStream ms = new MemoryStream())
                            {
                                if (_jpegCodec != null)
                                    resized.Save(ms, _jpegCodec, _encoderParams);
                                else
                                    resized.Save(ms, ImageFormat.Jpeg);

                                byte[] imgBytes = ms.ToArray();
                                output.Write(header, 0, header.Length);
                                output.Write(imgBytes, 0, imgBytes.Length);
                                output.Flush();
                            }
                        }
                    }

                    Thread.Sleep(33); // ~30 FPS low bandwidth pacing
                }
                catch
                {
                    break;
                }
            }
        }
    }
}
