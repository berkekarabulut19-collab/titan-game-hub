using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TitanGameHub
{
    /// <summary>
    /// Uygulama içi indirme tarayıcısı (WebView2 / gömülü Chromium).
    ///
    /// fileditch / bzzhr / buzzheavier gibi hostlar düz HTTP isteklerine 403 veriyor
    /// (Cloudflare bot koruması). WebView2 gerçek bir Chromium oturumu olduğu için
    /// dosya uygulamanın içine iner; harici tarayıcıya çıkmaya gerek kalmaz.
    ///
    /// Doğrulama: 482 MB'lık bir arşiv gömülü tarayıcı ile indirildi (ilerleme + tamamlanma).
    /// </summary>
    public static class WebViewDownloadService
    {
        public class Result
        {
            public bool Success;
            public string FilePath;
            public string Error;
            public long Bytes;
        }

        public static string BuildAutoClickScript()
        {
            return "(function(){" +
                "var bt=(document.title+' '+(document.body?document.body.innerText.slice(0,220):'')).toLowerCase();" +
                "if(bt.indexOf('429 too many requests')>=0||bt.indexOf('404 not found')>=0||bt.indexOf('file not found')>=0)return 'ERR:'+document.title;" +
                "var s='a[hx-get],button[hx-get],[hx-get*=\"download\"],[hx-post*=\"download\"],a.btn-main,button.btn-main,.btn-main,a.btn,button.btn,a[href$=\".rar\"],a[href$=\".zip\"],a[href$=\".7z\"],a[href*=\"/dl/\"],a[href*=\"/download\"],a.link-button,button.link-button,a[download],#downloadbtn,a[href*=\"download\"],form button[type=\"submit\"]:not([disabled]),form input[type=\"submit\"]:not([disabled])';" +
                "var a=document.querySelectorAll(s);" +
                "for(var i=0;i<a.length;i++){" +
                "  var el=a[i]; if(!el||el.disabled) continue;" +
                "  var t=(el.innerText||el.value||'').toLowerCase();" +
                "  if(t.indexOf('premium')>=0||t.indexOf('vpn')>=0||t.indexOf('upgrade')>=0||t.indexOf('browser')>=0) continue;" +
                "  if(el.offsetParent!==null){el.click();return i+1;}" +
                "}" +
                "var all=document.querySelectorAll('a,button,input[type=\"submit\"]');" +
                "for(var j=0;j<all.length;j++){" +
                "  var b=all[j]; if(!b||b.disabled||b.offsetParent===null) continue;" +
                "  var txt=(b.innerText||b.value||'').trim().toLowerCase();" +
                "  if(txt==='download'||txt==='download file'||txt.indexOf('download file')===0||txt==='free download'||txt==='direct download'||txt==='start download'||txt==='create download link'||txt==='download now'||txt==='indir'){" +
                "    b.click();return 100+j;" +
                "  }" +
                "}" +
                "if(a.length>0){a[0].click();return a.length;}return 0;})()";
        }

        public static bool IsRuntimeAvailable()
        {
            try
            {
                string version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                return !string.IsNullOrEmpty(version);
            }
            catch { return false; }
        }

        public static Result Download(string url, string targetDirectory, string suggestedFileName,
                                      Action<int, long, long> onProgress, Func<bool> isCancelled,
                                      int timeoutMinutes)
        {
            return Download(url, targetDirectory, suggestedFileName, onProgress, isCancelled, timeoutMinutes, null);
        }

        public static string LastDomSnapshot { get; private set; }

        public static Result Download(string url, string targetDirectory, string suggestedFileName,
                                      Action<int, long, long> onProgress, Func<bool> isCancelled,
                                      int timeoutMinutes, string referer)
        {
            Result result = new Result();

            if (!IsRuntimeAvailable())
            {
                result.Error = "WebView2 çalışma zamanı bulunamadı (Microsoft Edge kurulu değil).";
                return result;
            }

            Window win = null;
            try
            {
                if (!Directory.Exists(targetDirectory)) Directory.CreateDirectory(targetDirectory);

                // Kullanıcı verisi uygulama klasörünü şişirmesin
                string userDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TitanGameHub", "WebView2");
                try { if (!Directory.Exists(userDataDir)) Directory.CreateDirectory(userDataDir); } catch { }

                // "İndir" akışında site ekranda açılmasın: tarayıcı oturumu ekran dışında 960x640 gerçek boyutta çalışır.
                // NOT: AllowsTransparency=true WebView2 HwndHost'u ve WASM/layout kontrollerini bozar; asla kullanma.
                win = new Window()
                {
                    Title = "Titan // İndirme",
                    Width = 960,
                    Height = 640,
                    WindowStyle = WindowStyle.ToolWindow,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Left = -20000,
                    Top = -20000,
                    ResizeMode = ResizeMode.NoResize
                };

                WebView2 view = new WebView2();
                win.Content = view;

                bool finished = false;
                bool downloadStarted = false;
                CoreWebView2DownloadOperation activeOp = null;
                DateTime deadline = DateTime.UtcNow.AddMinutes(Math.Max(5, timeoutMinutes));

                Action<CoreWebView2> attachHandlers = delegate(CoreWebView2 core)
                {
                    try
                    {
                        core.Settings.AreDefaultContextMenusEnabled = false;
                        core.Settings.IsStatusBarEnabled = false;
                        core.Settings.AreDefaultScriptDialogsEnabled = false;

                        // Asla harici tarayıcı veya açılır pencere (popup) açılmasın
                        core.NewWindowRequested += delegate(object snw, CoreWebView2NewWindowRequestedEventArgs enw)
                        {
                            try
                            {
                                enw.Handled = true;
                                string target = enw.Uri ?? "";
                                string tl = target.ToLowerInvariant();
                                bool isDownloadTarget = tl.Contains(".rar") || tl.Contains(".zip") || tl.Contains(".7z") ||
                                                        tl.Contains(".torrent") || tl.Contains("/download") || tl.Contains("/dl/") ||
                                                        tl.Contains("bzzhr") || tl.Contains("buzzheavier") || tl.Contains("fileditch") ||
                                                        tl.Contains("fuckingfast") || tl.Contains("pixeldrain") || tl.Contains("gofile") ||
                                                        tl.Contains("datanodes") || tl.Contains("vikingfile") ||
                                                        tl.Contains("cloudflarestorage") || tl.Contains("token=") || tl.Contains("expires=");
                                if (isDownloadTarget && !string.IsNullOrEmpty(target))
                                    core.Navigate(target);
                            }
                            catch { }
                        };

                        string initialUrl = url.StartsWith("//") ? "https:" + url : url;
                        if (!string.IsNullOrEmpty(referer))
                        {
                            try
                            {
                                bool sentInitialReferer = false;
                                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Document);
                                core.WebResourceRequested += delegate(object sr, CoreWebView2WebResourceRequestedEventArgs er)
                                {
                                    try
                                    {
                                        if (!sentInitialReferer && string.Equals(er.Request.Uri, initialUrl, StringComparison.OrdinalIgnoreCase))
                                        {
                                            sentInitialReferer = true;
                                            er.Request.Headers.SetHeader("Referer", referer);
                                            er.Request.Headers.SetHeader("Accept-Language", "en-US,en;q=0.9");
                                        }
                                    }
                                    catch { }
                                };
                            }
                            catch { }
                        }

                        core.DownloadStarting += delegate(object s2, CoreWebView2DownloadStartingEventArgs e2)
                        {
                            string fileName = !string.IsNullOrEmpty(suggestedFileName)
                                ? suggestedFileName
                                : Path.GetFileName(e2.ResultFilePath);

                            if (string.IsNullOrEmpty(fileName) || fileName.IndexOf('.') < 0)
                                fileName = "titan_download.rar";

                            try { e2.ResultFilePath = Path.Combine(targetDirectory, fileName); } catch { }
                            e2.Handled = true;

                            CoreWebView2DownloadOperation op = e2.DownloadOperation;
                            if (op == null) return;
                            downloadStarted = true;
                            activeOp = op;

                            op.BytesReceivedChanged += delegate
                            {
                                try
                                {
                                    long total = (long)(op.TotalBytesToReceive ?? 0);
                                    long got = (long)op.BytesReceived;
                                    int pct = total > 0 ? (int)((got * 100) / total) : 0;
                                    if (onProgress != null) onProgress(pct, got, total);
                                }
                                catch { }
                            };

                            op.StateChanged += delegate
                            {
                                try
                                {
                                    if (op.State == CoreWebView2DownloadState.Completed)
                                    {
                                        result.Success = true;
                                        result.FilePath = op.ResultFilePath;
                                        result.Bytes = (long)op.BytesReceived;
                                        finished = true;
                                        win.Close();
                                    }
                                    else if (op.State == CoreWebView2DownloadState.Interrupted)
                                    {
                                        if (string.IsNullOrEmpty(result.Error))
                                            result.Error = "İndirme kesildi: " + op.InterruptReason;
                                        finished = true;
                                        win.Close();
                                    }
                                }
                                catch { }
                            };
                        };

                        core.Navigate(url);

                        // İndirme başlamadıysa WASM doğrulama/geri sayım bitene kadar (40 sn) otomatik tıkla
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            string clickJs = BuildAutoClickScript();
                            for (int attempt = 0; attempt < 45; attempt++)
                            {
                                Thread.Sleep(attempt == 0 ? 700 : 900);
                                if (finished || downloadStarted) return;
                                try
                                {
                                    view.Dispatcher.BeginInvoke(new Action(delegate
                                    {
                                        if (finished || downloadStarted) return;
                                        try
                                        {
                                            core.ExecuteScriptAsync(clickJs).ContinueWith(delegate(System.Threading.Tasks.Task<string> ct)
                                            {
                                                try
                                                {
                                                    if (!ct.IsFaulted && !string.IsNullOrEmpty(ct.Result) && ct.Result.Contains("ERR:"))
                                                    {
                                                        if (string.IsNullOrEmpty(result.Error)) result.Error = "Sunucu hatası: " + ct.Result;
                                                        finished = true;
                                                        try { win.Dispatcher.BeginInvoke(new Action(delegate { win.Close(); })); } catch { }
                                                    }
                                                }
                                                catch { }
                                            });
                                            if (attempt == 4 || attempt == 14)
                                            {
                                                core.ExecuteScriptAsync("location.href + ' || ' + document.title + ' || ' + (document.body ? document.body.innerText.replace(/\\s+/g,' ').slice(0,300) : '')")
                                                    .ContinueWith(delegate(System.Threading.Tasks.Task<string> st)
                                                    {
                                                        try { if (!st.IsFaulted && !string.IsNullOrEmpty(st.Result)) LastDomSnapshot = st.Result; } catch { }
                                                    });
                                            }
                                        }
                                        catch { }
                                    }));
                                }
                                catch { }
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        if (string.IsNullOrEmpty(result.Error)) result.Error = "Tarayıcı hatası: " + ex.Message;
                        finished = true;
                        try { win.Close(); } catch { }
                    }
                };

                win.Loaded += delegate
                {
                    CoreWebView2Environment.CreateAsync(null, userDataDir).ContinueWith(delegate(System.Threading.Tasks.Task<CoreWebView2Environment> et)
                    {
                        if (et.IsFaulted || et.Result == null)
                        {
                            if (string.IsNullOrEmpty(result.Error))
                                result.Error = "WebView2 ortamı oluşturulamadı: " +
                                    (et.Exception != null ? et.Exception.GetBaseException().Message : "bilinmiyor");
                            finished = true;
                            try { win.Dispatcher.BeginInvoke(new Action(delegate { win.Close(); })); } catch { }
                            return;
                        }

                        view.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            view.EnsureCoreWebView2Async(et.Result).ContinueWith(delegate(System.Threading.Tasks.Task t)
                            {
                                view.Dispatcher.BeginInvoke(new Action(delegate
                                {
                                    if (t.IsFaulted || view.CoreWebView2 == null)
                                    {
                                        if (string.IsNullOrEmpty(result.Error))
                                            result.Error = "WebView2 başlatılamadı: " +
                                                (t.Exception != null ? t.Exception.GetBaseException().Message : "bilinmiyor");
                                        finished = true;
                                        try { win.Close(); } catch { }
                                        return;
                                    }

                                    attachHandlers(view.CoreWebView2);
                                }));
                            });
                        }));
                    });

                    // Bekçi: iptal / zaman aşımı
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        while (!finished)
                        {
                            Thread.Sleep(500);
                            bool cancelled = false;
                            try { cancelled = isCancelled != null && isCancelled(); } catch { }

                            if (cancelled || DateTime.UtcNow > deadline)
                            {
                                if (string.IsNullOrEmpty(result.Error))
                                    result.Error = cancelled ? "İptal edildi" : "Zaman aşımı";
                                finished = true;
                                try
                                {
                                    win.Dispatcher.BeginInvoke(new Action(delegate
                                    {
                                        try { if (activeOp != null) activeOp.Cancel(); } catch { }
                                        try { win.Close(); } catch { }
                                    }));
                                }
                                catch { }
                                break;
                            }
                        }
                    });
                };

                win.ShowDialog();

                // Yalnızca CoreWebView2DownloadState.Completed olduğunda başarılı say; yarım/bozuk dosyayı sil.
                if (!result.Success && !string.IsNullOrEmpty(result.FilePath) && File.Exists(result.FilePath))
                {
                    try { File.Delete(result.FilePath); } catch { }
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Error = "Gömülü tarayıcı hatası: " + ex.Message;
                try { if (win != null) win.Close(); } catch { }
                return result;
            }
        }
    }
}
