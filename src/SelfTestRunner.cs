using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
namespace TitanGameHub
{
    /// <summary>
    /// Otomatik kendi kendini test: kritik motorların doğruluğunu ağ olmadan kontrol eder.
    /// Çalıştırma: TitanGameHub.exe --selftest
    /// Rapor: data/selftest_report.txt
    /// </summary>
    public static class SelfTestRunner
    {
        private static int _passed;
        private static int _failed;
        private static readonly StringBuilder Report = new StringBuilder();
        public static int Run()
        {
            _passed = 0; _failed = 0;
            Report.Length = 0;
            Report.AppendLine("TITAN GAME HUB — OTOMATİK TEST RAPORU");
            Report.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Report.AppendLine(new string('=', 60));
            TestDedupe();
            TestMirrorPolicy();
            TestHostPolicy();
            TestMultiPart();
            TestManifest();
            TestSignature();
            TestUrlHosts();
            TestVersionCompare();
            TestTorrentExtraction();
            TestPageUrlDetection();
            TestUpdateLedger();
            TestWebView2();
            TestAria2();
            TestMaintenance();
            TestLanguageAndApi();
            TestGameLoopPubgMobile();
            Report.AppendLine(new string('=', 60));
            Report.AppendLine(string.Format("SONUÇ: {0} geçti, {1} başarısız", _passed, _failed));
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "selftest_report.txt"), Report.ToString(), Encoding.UTF8);
            }
            catch { }
            return _failed == 0 ? 0 : 1;
        }
        private static void Check(string name, bool ok, string detail)
        {
            if (ok) _passed++; else _failed++;
            Report.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name + (string.IsNullOrEmpty(detail) ? "" : "  → " + detail));
        }
        private static void TestDedupe()
        {
            Report.AppendLine("\n-- Çift kayıt normalizasyonu --");
            Check("Valheim MULTi14-ElAmigos",
                GameDeduplicationEngine.NormalizeGameTitle("Valheim MULTi14-ElAmigos") ==
                GameDeduplicationEngine.NormalizeGameTitle("Valheim"), "");
            Check("Ready or Not: Digital Deluxe Edition, v142058 + 6 DLCs/Bonuses",
                GameDeduplicationEngine.NormalizeGameTitle("Ready or Not: Digital Deluxe Edition, v142058 (v1.5.3) + v66884 + 6 DLCs/Bonuses") ==
                GameDeduplicationEngine.NormalizeGameTitle("Ready or Not"), "");
            Check("Deep Rock Galactic: Deluxe Edition, v1.40 + 13 DLCs/Bonuses",
                GameDeduplicationEngine.NormalizeGameTitle("Deep Rock Galactic: Deluxe Edition, v1.40.158958.0 + 13 DLCs/Bonuses") ==
                GameDeduplicationEngine.NormalizeGameTitle("Deep Rock Galactic"), "");
            Check("Farklı oyunlar ayrı kalmalı",
                GameDeduplicationEngine.NormalizeGameTitle("Palworld") !=
                GameDeduplicationEngine.NormalizeGameTitle("Valheim"), "");
        }
        private static void TestMirrorPolicy()
        {
            Report.AppendLine("\n-- Ayna güven politikası --");
            Check("Yanlış oyun dosyası reddedilir",
                MirrorTrustPolicy.LooksLikeWrongGame("https://drop.download/x/Lawn.Mowing.Simulator.v16.08.2021.rar", "R.E.P.O."), "");
            Check("Doğru oyun dosyası kabul edilir",
                !MirrorTrustPolicy.LooksLikeWrongGame("https://f.st/a/Moon-Sweepers-SteamRIP.com.rar", "Moon Sweepers"), "");
            Check("Yazım hatalı eşleşme (Skedule ~ Schedule)",
                !MirrorTrustPolicy.LooksLikeWrongGame("https://f.st/a/Skedule-I-SteamRIP.com.rar", "Schedule I"), "");
            Check("ID tabanlı link (gofile) nötr",
                !MirrorTrustPolicy.LooksLikeWrongGame("https://gofile.io/d/jMI5xb", "Anything"), "");
        }
        private static void TestHostPolicy()
        {
            Report.AppendLine("\n-- Host politikası --");
            Check("pixeldrain doğrudan indirmeye çevrilir",
                DownloadHostPolicy.NormalizeDirectUrl("https://pixeldrain.com/u/HH9K444p") ==
                "https://pixeldrain.com/api/file/HH9K444p?download", "");
            Check("fileditch tarayıcı gerektirir",
                DownloadHostPolicy.NeedsBrowserSession("https://itsfileditchfiles.st/a/b.rar"), "");
            Check("bzzhr tarayıcı gerektirir",
                DownloadHostPolicy.NeedsBrowserSession("https://bzzhr.to/abc"), "");
            Check("pixeldrain tarayıcı gerektirmez",
                !DownloadHostPolicy.NeedsBrowserSession("https://pixeldrain.com/api/file/x?download"), "");
            Check("pixeldrain en yüksek hız puanı",
                DownloadHostPolicy.SpeedScore("https://pixeldrain.com/api/file/x?download") >
                DownloadHostPolicy.SpeedScore("https://itsfileditchfiles.st/a/b.rar"), "");
            Check("reklam kısaltıcılar elenir",
                DownloadHostPolicy.IsAdShortener("https://srnky.com/bd1QreiJs") &&
                DownloadHostPolicy.SpeedScore("https://shrinkme.click/Y1y1Jv") == 0, "");
        }
        private static void TestMultiPart()
        {
            Report.AppendLine("\n-- Çok parçalı arşiv --");
            List<string> parts = MultiPartDownloader.BuildPartCandidates("https://x/y/Game.part01.rar", 3);
            Check("part01 → part02/part03", parts.Count == 3 && parts[1].EndsWith("part02.rar") && parts[2].EndsWith("part03.rar"), "");
            List<string> parts2 = MultiPartDownloader.BuildPartCandidates("https://x/y/Game.001", 3);
            Check(".001 → .002/.003", parts2.Count == 3 && parts2[1].EndsWith(".002"), "");
            Check("Tek arşiv dokunulmaz", !MultiPartDownloader.IsPartUrl("https://x/y/Game.rar"), "");
        }
        private static void TestManifest()
        {
            Report.AppendLine("\n-- Bütünlük manifesti --");
            string root = Path.Combine(Path.GetTempPath(), "titan_selftest_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "bin"));
                File.WriteAllText(Path.Combine(root, "bin", "a.dll"), "AAAA");
                File.WriteAllText(Path.Combine(root, "bin", "b.exe"), "BBBBBBBB");
                GameProfile gp = new GameProfile { Id = "selftest", Name = "Self Test", WorkingDirectory = root };
                int saved = GameFileManifest.SaveSnapshot(gp);
                Check("Snapshot oluşturuldu", saved == 2, saved + " dosya");
                File.Delete(Path.Combine(root, "bin", "a.dll"));
                File.WriteAllText(Path.Combine(root, "bin", "b.exe"), "BOZUK");
                List<string> missing, changed;
                GameFileManifest.CompareToSnapshot(gp, out missing, out changed);
                Check("Silinen dosya tespit edildi", missing.Count == 1, string.Join(",", missing.ToArray()));
                Check("Boyutu değişen dosya tespit edildi", changed.Count == 1, string.Join(",", changed.ToArray()));
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }
        }
        private static void TestSignature()
        {
            Report.AppendLine("\n-- OTA imza doğrulaması --");
            string key = OtaSignatureVerifier.FindPrivateKeyPath();
            if (string.IsNullOrEmpty(key))
            {
                Report.AppendLine("[SKIP] Özel anahtar yok — imza testi atlandı (dağıtım sürümünde normal).");
                return;
            }
            string tempFile = Path.Combine(Path.GetTempPath(), "titan_sig_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".zip");
            try
            {
                File.WriteAllText(tempFile, "SELFTEST-PACKAGE-CONTENT");
                long ts = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
                string manifest, err;
                bool signed = OtaSignatureVerifier.TrySignPackage(tempFile, ts, out manifest, out err);
                Check("Paket imzalandı", signed, err);
                if (signed)
                {
                    long mts; string sha, sig;
                    Check("Manifest ayrıştırıldı", OtaSignatureVerifier.TryParseManifest(manifest, out mts, out sha, out sig), "");
                    string status;
                    Check("İmza geçerli", OtaSignatureVerifier.VerifySignature(mts, sha, sig, out status), status);
                    Check("Zaman damgası oynatılınca reddedilir", !OtaSignatureVerifier.VerifySignature(mts + 5, sha, sig, out status), "");
                    Check("SHA değiştirilince reddedilir", !OtaSignatureVerifier.VerifySignature(mts, new string('a', 64), sig, out status), "");
                }
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }
        private static void TestUrlHosts()
        {
            Report.AppendLine("\n-- URL ekleme host kontrolü --");
            Check("steamrip kabul", SiteUrlImporter.IsSupportedHost("https://steamrip.com/valheim-free-download/"), "");
            Check("fitgirl kabul", SiteUrlImporter.IsSupportedHost("https://fitgirl-repacks.site/x/"), "");
            Check("yabancı site red", !SiteUrlImporter.IsSupportedHost("https://example.com/game"), "");
            Check("geçersiz URL red", !SiteUrlImporter.IsSupportedHost("not a url"), "");
        }
        private static void TestTorrentExtraction()
        {
            Report.AppendLine("\n-- Torrent magnet çıkarma --");
            string html1 = "<a href=\"magnet:?xt=urn:btih:ABCDEF1234567890ABCDEF1234567890ABCDEF12&dn=Game\">magnet</a>";
            string got1 = TorrentDownloadService.ExtractMagnetOrTorrent(html1, "https://steamrip.com/x/");
            Check("magnet linki bulundu", !string.IsNullOrEmpty(got1) && got1.StartsWith("magnet:"), got1 ?? "-");
            string html2 = "<a href=\"/torrents/game.torrent\">torrent</a>";
            string got2 = TorrentDownloadService.ExtractMagnetOrTorrent(html2, "https://steamrip.com/x/");
            Check(".torrent linki mutlaklaştırıldı", got2 == "https://steamrip.com/torrents/game.torrent", got2 ?? "-");
            string html3 = "<a href=\"//tracker.example/torrents/game.torrent\">t</a>";
            string got3 = TorrentDownloadService.ExtractMagnetOrTorrent(html3, "https://steamrip.com/x/");
            Check("protokol-göreli .torrent", got3 == "https://tracker.example/torrents/game.torrent", got3 ?? "-");
            string got4 = TorrentDownloadService.ExtractMagnetOrTorrent("<div>yok</div>", "https://x/");
            Check("link yoksa null", got4 == null, "");
        }
        private static void TestVersionCompare()
        {
            Report.AppendLine("\n-- Sürüm karşılaştırma --");
            Check("1.0.10 > 1.0.9", MultiSourceVersionResolver.CompareVersions("1.0.10", "1.0.9") > 0, "");
            Check("1.2 = 1.2.0", MultiSourceVersionResolver.CompareVersions("1.2", "1.2.0") == 0, "");
            Check("26317497 > 25000000", MultiSourceVersionResolver.CompareVersions("26317497", "25000000") > 0, "");
            Check("Sürüm çıkarma (v1.0.4.102642)", MultiSourceVersionResolver.ExtractVersion("Palworld – v1.0.4.102642 + DLC") == "1.0.4.102642", "");
        }

        private static void TestPageUrlDetection()
        {
            Report.AppendLine("\n-- Sayfa/arşiv linki ayrımı --");
            Check("SteamRIP sayfası sayfa olarak tanınır",
                DownloadInstallService.IsLikelyPageUrl("https://steamrip.com/moon-sweepers-free-download/"), "");
            Check("fileditch arşivi sayfa DEĞİL",
                !DownloadInstallService.IsLikelyPageUrl("https://itsfileditchfiles.st/a/b/Game.rar"), "");
            Check("gofile klasörü sayfa DEĞİL",
                !DownloadInstallService.IsLikelyPageUrl("https://gofile.io/d/AbC123"), "");
        }

        private static void TestUpdateLedger()
        {
            Report.AppendLine("\n-- Güncelleme defteri (tekrar uyarı yok) --");
            GameProfile gp = new GameProfile { Id = "selftest-ledger", Name = "Ledger Test", Version = "v1.2.3", RemoteVersion = "v1.2.3" };
            UpdateLedger.MarkUpToDate(gp, "v1.2.3");

            Check("Kurulan uzak sürüm 'güncel' sayılır", UpdateLedger.IsUpToDate(gp, "v1.2.3"), "");
            Check("Farklı uzak sürüm güncel SAYILMAZ", !UpdateLedger.IsUpToDate(gp, "v9.9.9"), "");
        }

        private static void TestWebView2()
        {
            Report.AppendLine("\n-- Gömülü tarayıcı (WebView2) --");
            try
            {
                bool ok = WebViewDownloadService.IsRuntimeAvailable();
                if (ok) Check("WebView2 çalışma zamanı hazır", true, "");
                else Report.AppendLine("[SKIP] WebView2 çalışma zamanı yok (Edge kurulu değil) — harici tarayıcıya düşülür.");
            }
            catch (Exception ex)
            {
                Report.AppendLine("[SKIP] WebView2 kontrolü yapılamadı: " + ex.Message);
            }
        }

        private static void TestAria2()
        {
            Report.AppendLine("\n-- Torrent motoru (aria2) --");
            string exe = TorrentDownloadService.FindAria2();
            if (string.IsNullOrEmpty(exe))
            {
                Report.AppendLine("[SKIP] aria2c bulunamadi (bin\\aria2c.exe veya D:\\Apps\\aria2).");
                return;
            }
            Check("aria2c bulundu", true, exe);

            string avail;
            bool ok = TorrentDownloadService.IsAvailable(out avail);
            Check("aria2 RPC hazır", ok, avail);
            if (ok) TorrentDownloadService.ShutdownEngine();
        }

        private static void TestMaintenance()
        {
            Report.AppendLine("\n-- Bakım servisleri --");

            GameProfile gp = new GameProfile { Id = "selftest-magnet", Name = "Magnet Test" };
            MagnetCatalog.Put(gp, "magnet:?xt=urn:btih:AAAABBBBCCCCDDDDEEEEFFFF0000111122223333");
            string got = MagnetCatalog.Get(gp);
            Check("Magnet kataloğa yazıldı", !string.IsNullOrEmpty(got) && got.StartsWith("magnet:"), got ?? "-");

            GameMaintenanceService.DiskReport rep = GameMaintenanceService.BuildDiskReport(new List<GameProfile>());
            Check("Disk raporu üretildi", !string.IsNullOrEmpty(rep.Text), "");

            string crash = CrashReporter.Report(new InvalidOperationException("selftest"), "Selftest");
            Check("Çökme raporu yazıldı", !string.IsNullOrEmpty(crash) && File.Exists(crash), crash ?? "-");
            try { if (!string.IsNullOrEmpty(crash)) File.Delete(crash); } catch { }
        }

        private static void TestLanguageAndApi()
        {
            Report.AppendLine("\n-- Dil / Katalog API --");

            LanguageService.Current = "en";
            Check("EN çeviri çalışıyor", LanguageService.T("OYNA") == "PLAY", LanguageService.T("OYNA"));
            Check("Bilinmeyen metin korunur", LanguageService.T("Bilinmeyen Metin") == "Bilinmeyen Metin", "");
            LanguageService.Current = "tr";
            Check("TR modunda değişmez", LanguageService.T("OYNA") == "OYNA", "");

            Report.AppendLine("[INFO] Katalog API: " + (CatalogApiClient.IsConfigured ? "yapılandırılmış" : "tanımlı değil (site taraması kullanılıyor)"));
        }

        private static void TestGameLoopPubgMobile()
        {
            Report.AppendLine("\n-- GameLoop PUBG Mobile Entegrasyonu --");
            Check("GameLoop resmi installer URL'si engellenmez",
                !MirrorTrustPolicy.LooksLikeWrongGame("https://down.gameloop.com/channel/88/16229/GLP_installer_1000218273_com.tencent.ig.exe", "PUBG Mobile (GameLoop)"),
                "");

            string glExe, glDir;
            bool detected = ProfileStore.TryResolveGameLoopPubgMobile(out glExe, out glDir);
            if (detected)
            {
                Check("GameLoop AppMarket.exe mevcut", File.Exists(glExe), glExe);
                GameProfile gp = new GameProfile
                {
                    Id = "pubg-mobile-gameloop",
                    Name = "PUBG Mobile (GameLoop)",
                    ExecutablePath = glExe,
                    WorkingDirectory = glDir
                };
                PreFlightDoctorResult dr = PreFlightLaunchDoctor.DiagnoseAndPrepareLaunch(gp);
                Check("GameLoop PreFlightLaunchDoctor hazır", dr != null && dr.ReadyToLaunch, dr != null ? dr.DiagnosticSummary : "");
            }
            else
            {
                Report.AppendLine("[INFO] Bu PC'de GameLoop + PUBG Mobile algilanmadi (temiz kurulum modu aktif).");
            }
        }
    }
}