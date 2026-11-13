using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TitanGameHub
{
    /// <summary>
    /// Araçlar sekmesi davranışları (kod bölme): URL/toplu katalog ekleme,
    /// torrent indirme, ağ teşhisi ve evrensel oyun tarama burada.
    /// </summary>
    public partial class WpfMainWindow
    {
        private void StartTorrentDownloadForSelected()
        {
            if (_selectedProfile == null)
            {
                MessageBox.Show("Önce kütüphaneden bir oyun seçin.", "Torrent", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string availMsg;
            if (!TorrentDownloadService.IsAvailable(out availMsg))
            {
                MessageBox.Show(
                    availMsg + "\n\nKURULUM (tek seferlik):\n" +
                    "1) qBittorrent'i kurun ve Web UI'yi (Araçlar > Seçenekler > Web UI) açın\n" +
                    "2) Kullanıcı/şifre belirleyin (varsayılan: admin / sifre)\n" +
                    "3) bin\\titan_user.cfg dosyasina siguiente satirari ekleyin:\n" +
                    "   QBIT_URL=http://127.0.0.1:8080\n   QBIT_USER=admin\n   QBIT_PASS=sifreniz\n" +
                    "   (yoksa qBittorrent indirme/seed durdur otomatik olarak devreye girecek)",
                    "Titan // Torrent", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            GameProfile gp = _selectedProfile;
            _txtBottomStatus.Text = "🧲 Magnet/.torrent bağlantısı aranıyor...";

            ThreadPool.QueueUserWorkItem(delegate
            {
                string magnet = null;
                try
                {
                    if (!string.IsNullOrEmpty(gp.DirectMirrorUrl) && gp.DirectMirrorUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                        magnet = gp.DirectMirrorUrl;
                    else if (!string.IsNullOrEmpty(gp.DownloadUrl) && gp.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        using (System.Net.WebClient wc = new System.Net.WebClient())
                        {
                            wc.Proxy = null;
                            wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                            string html = wc.DownloadString(gp.DownloadUrl);
                            magnet = TorrentDownloadService.ExtractMagnetOrTorrent(html, gp.DownloadUrl);
                        }
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(magnet))
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _txtBottomStatus.Text = "Bu oyunun sayfasında magnet/.torrent bulunamadı.";
                        MessageBox.Show("Sayfada magnet veya .torrent bağlantısı yok. Normal indirme kullanın.",
                            "Titan // Torrent", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }));
                    return;
                }

                string savePath = System.IO.Path.Combine(DownloadInstallService.GetEffectiveGamesRoot(), "_torrent");
                TorrentDownloadService.TorrentProgress tp = TorrentDownloadService.Download(
                    magnet, savePath,
                    delegate(TorrentDownloadService.TorrentProgress p)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            _txtDownloadStatus.Text = string.Format("🧲 {0} — %{1:0.0} • {2:0.00}/{3:0.00} GB • {4:0.0} MB/sn",
                                gp.Name, p.Percent, p.Downloaded / 1073741824.0, p.Size / 1073741824.0, p.SpeedBytesPerSec / 1048576.0);
                            if (_pbDownload != null) _pbDownload.Value = Math.Min(95, p.Percent);
                        }));
                    },
                    delegate { return false; },
                    240);

                if (!tp.Completed)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _txtBottomStatus.Text = "Torrent tamamlanmadı: " + (tp.Error ?? tp.State);
                    }));
                    return;
                }

                string archive = TorrentDownloadService.FindArchiveInContent(tp.ContentPath);
                if (string.IsNullOrEmpty(archive))
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _txtBottomStatus.Text = "Torrent indi ancak içinde .rar/.zip/.7z bulunamadı.";
                    }));
                    return;
                }

                string scanMsg;
                if (!ArchiveSecurityScanner.IsSafe(archive, out scanMsg))
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _txtBottomStatus.Text = "⛔ " + scanMsg;
                        MessageBox.Show(scanMsg, "Titan // Güvenlik", MessageBoxButton.OK, MessageBoxImage.Error);
                    }));
                    return;
                }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _txtBottomStatus.Text = "🧲 Torrent indi, arşiv kuruluyor: " + System.IO.Path.GetFileName(archive);
                    _downloader.ExtractAndBindAsync(gp, archive);
                }));
            });
        }

        private void RunNetworkDiagnostics()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("AĞ & CO-OP TEŞHİSİ");
                sb.AppendLine(new string('-', 44));

                try
                {
                    SteamAccountInfo acc = SteamClientBridgeService.GetActiveSteamAccount(true);
                    sb.AppendLine("Steam çalışıyor   : " + (acc.IsSteamRunning ? "EVET" : "HAYIR  ← Steam açık olmalı!"));
                    sb.AppendLine("Steam hesabı      : " + (string.IsNullOrEmpty(acc.PersonaName) ? "-" : acc.PersonaName));
                }
                catch { }

                try
                {
                    sb.AppendLine("Yerel IP          : " + VirtualLanMeshService.ActiveVirtualIp);
                }
                catch { }

                try
                {
                    StunNatResult nat = ZeroVpnTunnelService.DiscoverPublicEndpoint(0);
                    if (nat != null && nat.Success)
                    {
                        sb.AppendLine("Genel IP          : " + nat.PublicIp + ":" + nat.PublicPort);
                        sb.AppendLine("Yerel port        : " + nat.LocalBoundPort);
                        bool samePort = nat.PublicPort == nat.LocalBoundPort;
                        sb.AppendLine("NAT tipi          : " + (samePort ? "AÇIK (endpoint bağımsız) — P2P uygun" : "SİMETRİK/KISITLI — hole punching zor"));
                    }
                    else
                    {
                        sb.AppendLine("Genel IP          : alınamadı (STUN engelli)");
                        sb.AppendLine("NAT tipi          : bilinmiyor");
                    }
                }
                catch { }

                try
                {
                    sb.AppendLine("UPnP eşlemesi     : " + (UpnpPortForwarderService.ActiveMappings != null && UpnpPortForwarderService.ActiveMappings.Count > 0
                        ? (UpnpPortForwarderService.ActiveMappings.Count + " aktif kural")
                        : "yok (router'da UPnP kapalı olabilir)"));
                }
                catch { }

                try
                {
                    bool lobbyRunning = _cloudLobby != null && _cloudLobby.IsRunning;
                    int onlineCount = 0;
                    if (_cloudLobby != null)
                    {
                        var fl = _cloudLobby.GetLiveFriendsList();
                        if (fl != null) onlineCount = fl.Count;
                    }
                    sb.AppendLine("Online lobi       : " + (lobbyRunning ? "AKTİF (senkron çalışıyor)" : "PASİF"));
                    sb.AppendLine("Lobi kodu         : " + (_cloudLobby != null ? _cloudLobby.RoomCode : "-"));
                    sb.AppendLine("Arkadaş kodu      : " + (_cloudLobby != null ? _cloudLobby.MyFriendCode : "-"));
                    sb.AppendLine("Kullanıcı adı     : " + (_cloudLobby != null ? _cloudLobby.UserName : "-"));
                    sb.AppendLine("Mevcut durum      : " + (_cloudLobby != null ? (_cloudLobby.CurrentStatus + " — " + _cloudLobby.CurrentGame) : "-"));
                    sb.AppendLine("Çevrim içi arkadaş: " + onlineCount + " kişi");
                    if (_cloudLobby != null && _cloudLobby.LastHeartbeatUtc != DateTime.MinValue)
                        sb.AppendLine("Son heartbeat     : " + _cloudLobby.LastHeartbeatUtc.ToLocalTime().ToString("HH:mm:ss") + " (yerel)");
                }
                catch { }

                sb.AppendLine();
                sb.AppendLine("ÖNERİ:");
                sb.AppendLine("• Steam kapalıysa co-op çalışmaz (Spacewar 480).");
                sb.AppendLine("• NAT simetrikse 'TAKIMI EŞİTLE & P2P GÜNCELLE' deneyin.");
                sb.AppendLine("• Router'da UPnP'yi açın veya modemde port yönlendirin.");
                sb.AppendLine("• VPN açıksa kapatın (hole punching engellenir).");

                string text = sb.ToString();
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    MessageBox.Show(text, "Titan // Ağ Teşhisi", MessageBoxButton.OK, MessageBoxImage.Information);
                }));
            });
        }

        private void RunBulkCatalogImport(int pagesPerSource)
        {
            if (_bulkImportRunning)
            {
                _txtBottomStatus.Text = "Toplu katalog aktarımı zaten çalışıyor...";
                return;
            }

            _bulkImportRunning = true;
            _txtBottomStatus.Text = "⬇ Bağlı sitelerin katalogları taranıyor (kapaklar dahil)...";
            Win32.PlayTechChirp(true);

            ThreadPool.QueueUserWorkItem(delegate
            {
                BulkCatalogImporter.ImportSummary sum = null;
                string error = null;
                try
                {
                    sum = BulkCatalogImporter.Import(_allProfiles, pagesPerSource, 50, delegate(string msg, int pct)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (_txtBottomStatus != null) _txtBottomStatus.Text = "⬇ " + msg;
                            if (_pbDownload != null) _pbDownload.Value = pct;
                        }));
                    });

                    ProfileStore.SaveProfiles(_allProfiles);
                }
                catch (Exception ex) { error = ex.Message; }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _bulkImportRunning = false;

                    if (error != null)
                    {
                        _txtBottomStatus.Text = "Toplu aktarım hatası: " + error;
                        MessageBox.Show(error, "Titan // Toplu Katalog", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    RefreshLibrarySidebarList();
                    RefreshLibrarySummaryMetrics();
                    RefreshStorefrontGrid();
                    ApplyLibraryFilter();

                    _txtBottomStatus.Text = "✔ Toplu katalog: " + sum.Text;
                    Win32.PlayTechChirp(true);

                    MessageBox.Show(
                        "TOPLU KATALOG AKTARIMI TAMAMLANDI\n\n" + sum.Text +
                        "\n\nKapak görselleri eksik olanlar listede tutulmaz, eksikler monogramla gösterilir.",
                        "Titan // Toplu Katalog", MessageBoxButton.OK, MessageBoxImage.Information);
                }));
            });
        }

        private void RunBulkImportHeadless()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                StringBuilder log = new StringBuilder();
                log.AppendLine("=== TITAN TOPLU KATALOG AKTARIMI ===");
                log.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                BulkCatalogImporter.ImportSummary sum = null;
                try
                {
                    sum = BulkCatalogImporter.Import(_allProfiles, 3, 50, delegate(string msg, int pct)
                    {
                        log.AppendLine(string.Format("[{0,3}%] {1}", pct, msg));
                    });
                    ProfileStore.SaveProfiles(_allProfiles);
                    log.AppendLine();
                    log.AppendLine("SONUC: " + sum.Text);
                    foreach (string l in sum.Log) log.AppendLine("  - " + l);

                    int covers = 0, installed = 0;
                    foreach (GameProfile gp in _allProfiles)
                    {
                        if (!string.IsNullOrEmpty(gp.BannerUrl)) covers++;
                        if (gp.IsInstalled) installed++;
                    }
                    log.AppendLine(string.Format("KATALOG: {0} oyun • kapaklı: {1} • kurulu: {2}", _allProfiles.Count, covers, installed));
                }
                catch (Exception ex)
                {
                    log.AppendLine("HATA: " + ex);
                }

                try
                {
                    string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                    if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "bulk_import_log.txt"), log.ToString(), Encoding.UTF8);
                }
                catch { }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    try { this.Close(); } catch { }
                    Environment.Exit(0);
                }));
            });
        }

        private void ImportGameFromUrlDialog()
        {
            string url = TextPromptDialog.Prompt(this, "Titan // URL'den Oyun Ekle",
                "Desteklenen bir oyun sayfasının linkini yapıştırın:",
                "https://");
            if (string.IsNullOrWhiteSpace(url)) return;

            _txtBottomStatus.Text = "🔎 Sayfa okunuyor, oyun bilgileri ve kapağı çekiliyor...";

            ThreadPool.QueueUserWorkItem(delegate
            {
                string msg;
                GameProfile gp = SiteUrlImporter.Import(url, out msg);

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (gp == null)
                    {
                        _txtBottomStatus.Text = "URL'den ekleme başarısız: " + msg;
                        MessageBox.Show(msg, "Titan // URL'den Ekle", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    GameProfile canonical = EnsureGameInLibraryCatalog(gp);
                    ProfileStore.SaveProfiles(_allProfiles);
                    RefreshLibrarySidebarList();
                    RefreshLibrarySummaryMetrics();
                    RefreshStorefrontGrid();

                    if (canonical != null) SelectGameProfile(canonical);
                    _txtBottomStatus.Text = msg.Replace("\n", "  •  ");
                    Win32.PlayTechChirp(true);
                    MessageBox.Show(msg, "Titan // Kütüphaneye Eklendi", MessageBoxButton.OK, MessageBoxImage.Information);
                }));
            });
        }

        /// <summary>Arayüzsüz kapak tamamlama (test/otomasyon): --fill-covers</summary>
        private void RunCoverBackfillHeadless(int limit)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                StringBuilder log = new StringBuilder();
                log.AppendLine("=== KAPAK TAMAMLAMA ===");
                log.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                try
                {
                    CoverBackfillService.Result r = CoverBackfillService.Run(_allProfiles, limit,
                        delegate(string m, int p) { log.AppendLine(string.Format("[{0,3}%] {1}", p, m)); });

                    ProfileStore.SaveProfiles(_allProfiles);

                    int withCover = 0;
                    foreach (GameProfile gp in _allProfiles) if (!string.IsNullOrEmpty(gp.BannerUrl)) withCover++;

                    log.AppendLine();
                    log.AppendLine(string.Format("SONUC: katalog={0} kapakli={1} eksikti={2} eklendi={3}",
                        _allProfiles.Count, withCover, r.Missing, r.Filled));
                    if (r.Unresolved.Count > 0)
                    {
                        log.AppendLine("Steam'de bulunamayanlar (ilk 40):");
                        foreach (string n in r.Unresolved) log.AppendLine("  - " + n);
                    }
                }
                catch (Exception ex) { log.AppendLine("HATA: " + ex); }

                try
                {
                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "cover_backfill_log.txt"), log.ToString(), Encoding.UTF8);
                }
                catch { }

                Dispatcher.BeginInvoke(new Action(delegate { try { this.Close(); } catch { } Environment.Exit(0); }));
            });
        }

        /// <summary>Test: gömülü tarayıcı indirmesi gerçekten bayt alıyor mu? (--wv2-test URL)</summary>
        /// <summary>Test: aria2 motoru ve indirme akışı (--torrent-test=URL|magnet)</summary>
        private void RunTorrentTest(string source)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                StringBuilder log = new StringBuilder();
                log.AppendLine("=== ARIA2 / TORRENT TESTI ===");
                log.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                log.AppendLine("Kaynak: " + source);

                string msg;
                log.AppendLine("Motor: " + (TorrentDownloadService.IsAvailable(out msg) ? "HAZIR" : "YOK") + " — " + msg);
                log.AppendLine("aria2c: " + (TorrentDownloadService.FindAria2() ?? "bulunamadı"));

                try
                {
                    string dir = Path.Combine(DownloadInstallService.GetEffectiveGamesRoot(), "_torrent_test2");
                    long max = 0;
                    int events = 0;

                    TorrentDownloadService.TorrentProgress tp = TorrentDownloadService.Download(source, dir,
                        delegate(TorrentDownloadService.TorrentProgress p)
                        {
                            events++;
                            if (p.Downloaded > max) max = p.Downloaded;
                            if (events <= 3)
                                log.AppendLine(string.Format("  ilerleme: %{0} — {1:0.00} MB / {2:0.00} MB @ {3:0} KB/s",
                                    p.Percent, p.Downloaded / 1048576.0, p.Size / 1048576.0, p.SpeedBytesPerSec / 1024));
                        },
                        delegate { return max > 60L * 1024 * 1024; },   // 60 MB sonra iptal (test)
                        10);

                    log.AppendLine();
                    log.AppendLine("Durum: " + (tp.Completed ? "TAMAMLANDI" : "TAMAMLANMADI"));
                    log.AppendLine("İndirilen: " + (tp.Downloaded / 1048576) + " MB / " + (tp.Size / 1048576) + " MB");
                    log.AppendLine("Olay: " + events);
                    log.AppendLine("Dosya: " + (tp.ContentPath ?? "-"));
                    log.AppendLine("Hata: " + (tp.Error ?? "-"));
                    log.AppendLine("ÇALIŞIYOR mu: " + (max > 0 ? "EVET (bayt akışı var)" : "HAYIR"));
                }
                catch (Exception ex) { log.AppendLine("HATA: " + ex); }

                try
                {
                    string d2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                    if (!Directory.Exists(d2)) Directory.CreateDirectory(d2);
                    File.WriteAllText(Path.Combine(d2, "torrent_test.txt"), log.ToString(), Encoding.UTF8);
                }
                catch { }

                Dispatcher.BeginInvoke(new Action(delegate { try { this.Close(); } catch { } Environment.Exit(0); }));
            });
        }

        /// <summary>Arayüzsüz magnet tarama (test): --magnet-scan=N</summary>
        private void RunMagnetScanHeadless(int limit)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                StringBuilder log = new StringBuilder();
                log.AppendLine("=== MAGNET TARAMA ===");
                log.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                try
                {
                    int before = MagnetCatalog.Count();
                    int found = MagnetCatalog.BuildForCatalog(_allProfiles, limit,
                        delegate(string m, int p) { log.AppendLine(string.Format("[{0,3}%] {1}", p, m)); });
                    log.AppendLine();
                    log.AppendLine(string.Format("SONUC: once={0} yeni={1} toplam={2}", before, found, MagnetCatalog.Count()));
                }
                catch (Exception ex) { log.AppendLine("HATA: " + ex); }

                try
                {
                    string d = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                    if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                    File.WriteAllText(Path.Combine(d, "magnet_scan_log.txt"), log.ToString(), Encoding.UTF8);
                }
                catch { }

                Dispatcher.BeginInvoke(new Action(delegate { try { this.Close(); } catch { } Environment.Exit(0); }));
            });
        }

        private void RunWebViewDownloadTest(string url)
        {
            string testReferer = null;
            int sep = url.IndexOf('|');
            if (sep > 0)
            {
                testReferer = url.Substring(sep + 1);
                url = url.Substring(0, sep);
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                StringBuilder log = new StringBuilder();
                log.AppendLine("=== WEBVIEW2 INDIRME TESTI ===");
                log.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                log.AppendLine("URL: " + url);
                log.AppendLine("Runtime: " + (WebViewDownloadService.IsRuntimeAvailable() ? "VAR" : "YOK"));

                long firstBytes = 0;
                int events = 0;
                DateTime start = DateTime.UtcNow;

                try
                {
                    string dir = Path.Combine(DownloadInstallService.GetEffectiveGamesRoot(), "_wv2test");

                    WebViewDownloadService.Result r = null;
                    Dispatcher.Invoke(new Action(delegate
                    {
                        r = WebViewDownloadService.Download(url, dir, null,
                            delegate(int pct, long got, long total)
                            {
                                events++;
                                if (firstBytes == 0) firstBytes = got;
                                if (events <= 3)
                                    log.AppendLine(string.Format("  ilerleme: %{0} — {1:0.00} MB / {2:0.00} MB", pct, got / 1048576.0, total / 1048576.0));
                            },
                            delegate { return (DateTime.UtcNow - start).TotalSeconds > 40; },   // 40 sn sonra iptal
                            3, testReferer);
                    }));

                    log.AppendLine();
                    log.AppendLine("Sonuç: " + (r != null && r.Success ? "TAMAMLANDI" : "TAMAMLANMADI"));
                    log.AppendLine("Bayt alındı: " + (r != null ? r.Bytes : firstBytes));
                    log.AppendLine("İlerleme olayı: " + events);
                    log.AppendLine("Dosya: " + (r != null ? (r.FilePath ?? "-") : "-"));
                    log.AppendLine("Hata: " + (r != null ? (r.Error ?? "-") : "-"));
                    log.AppendLine("İndirme ÇALIŞIYOR mu: " + ((firstBytes > 0 || (r != null && r.Bytes > 0)) ? "EVET (bayt akışı var)" : "HAYIR"));
                }
                catch (Exception ex) { log.AppendLine("HATA: " + ex); }

                try
                {
                    string dir2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                    if (!Directory.Exists(dir2)) Directory.CreateDirectory(dir2);
                    File.WriteAllText(Path.Combine(dir2, "wv2_test.txt"), log.ToString(), Encoding.UTF8);
                }
                catch { }

                Dispatcher.BeginInvoke(new Action(delegate { try { this.Close(); } catch { } Environment.Exit(0); }));
            });
        }

        private void RunUniversalGameScan()
        {
            if (_txtBottomStatus != null)
                _txtBottomStatus.Text = "🔍 Tüm diskler ve oyun kütüphaneleri taranıyor... (Steam / Epic / GOG / portatif)";
            Win32.PlayTechChirp(true);

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<DiscoveredGameEntry> disc = new List<DiscoveredGameEntry>();
                string scanError = null;
                try { disc = UniversalGameScannerService.ScanInstalledGames(); }
                catch (Exception ex) { scanError = ex.Message; }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (scanError != null)
                    {
                        if (_txtBottomStatus != null) _txtBottomStatus.Text = "Tarama hatası: " + scanError;
                        return;
                    }

                    if (disc == null || disc.Count == 0)
                    {
                        if (_txtBottomStatus != null) _txtBottomStatus.Text = "🔍 Tarama bitti: kurulu oyun bulunamadı.";
                        MessageBox.Show("Bu bilgisayarda kurulu oyun klasörü tespit edilemedi.\n\nİpucu: oyunu 'OYUN EKLE' ile tek tek .exe üzerinden de bağlayabilirsiniz.",
                            "Titan // Tüm Oyunları Tara", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    string rep;
                    int added = UniversalGameScannerService.ImportDiscoveredGames(disc, _allProfiles, out rep);

                    RefreshLibrarySidebarList();
                    RefreshLibrarySummaryMetrics();
                    RefreshStorefrontGrid();

                    int installedNow = 0;
                    for (int i = 0; i < _allProfiles.Count; i++) { if (_allProfiles[i] != null && _allProfiles[i].IsInstalled) installedNow++; }

                    string summary = string.Format("🔍 Tarama tamamlandı: {0} oyun bulundu, {1} yeni oyun kütüphaneye eklendi. (Toplam yüklü: {2})",
                        disc.Count, added, installedNow);
                    if (_txtBottomStatus != null) _txtBottomStatus.Text = summary;
                    Win32.PlayTechChirp(true);

                    if (added > 0)
                    {
                        MessageBox.Show(summary + "\n\nYeni oyunlar listede 'BİLGİSAYARDA YÜKLÜ' bölümünde. Oynamak için üzerine tıklayıp OYNA'ya basın.",
                            "Titan // Tüm Oyunları Tara", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }));
            });
        }

    }
}
