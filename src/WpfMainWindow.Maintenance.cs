using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TitanGameHub
{
    /// <summary>
    /// Bakım & indirme yardımcıları: manuel link, oyun kaldırma, disk raporu,
    /// bant sınırı, magnet taraması, zamanlanmış indirme.
    /// </summary>
    public partial class WpfMainWindow
    {
        /// <summary>Manuel link: kullanıcı doğrudan arşiv linkini yapıştırır, uygulama indirip kurar.</summary>
        private void RunManualLinkInstall()
        {
            if (_selectedProfile == null)
            {
                MessageBox.Show("Önce kütüphaneden bir oyun seçin.", "Manuel Link", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string url = TextPromptDialog.Prompt(this, "Titan // Manuel Link ile İndir",
                "Doğrudan arşiv linkini (veya magnet/.torrent) yapıştırın.\nÖrnek: https://.../Game.rar  •  magnet:?xt=urn:btih:...",
                "https://");
            if (string.IsNullOrWhiteSpace(url)) return;

            url = url.Trim();

            if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
            {
                MagnetCatalog.Put(_selectedProfile, url);
                _txtBottomStatus.Text = "🧲 Magnet kaydedildi, torrent ile indirilecek...";
                StartTorrentDownloadForSelected();
                return;
            }

            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Geçerli bir http(s) veya magnet linki girin.", "Manuel Link", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _txtBottomStatus.Text = "⬇ Manuel link ile indirme başlatıldı...";
            _inAppDlStripBorder.Visibility = Visibility.Visible;
            _downloader.DownloadAndInstallAsync(_selectedProfile, url);
        }

        /// <summary>Oyunu kaldır (dosyalar + katalog kaydı).</summary>
        private void RunUninstallSelected()
        {
            if (_selectedProfile == null)
            {
                MessageBox.Show("Önce bir oyun seçin.", "Kaldır", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            GameProfile gp = _selectedProfile;
            if (SmartUpdateShieldService.IsGameProcessRunning(gp))
            {
                MessageBox.Show("Oyun çalışıyor. Önce kapatın.", "Kaldır", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBoxResult res = MessageBox.Show(
                "'" + gp.Name + "' kaldırılsın mı?\n\nEVET: oyun dosyaları diskten silinir\nHAYIR: sadece katalog kaydı silinir",
                "Titan // Oyunu Kaldır", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            if (res == MessageBoxResult.Cancel) return;

            string msg;
            bool ok = GameMaintenanceService.Uninstall(gp, res == MessageBoxResult.Yes, out msg);

            _allProfiles = ProfileStore.LoadProfiles();
            SelectGameProfile(_allProfiles.Count > 0 ? _allProfiles[0] : null);
            ApplyLibraryFilter();
            RefreshLibrarySidebarList();
            RefreshLibrarySummaryMetrics();
            RefreshStorefrontGrid();
            _txtBottomStatus.Text = msg;
            Win32.PlayTechChirp(ok);
            MessageBox.Show(msg, "Kaldırma", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        /// <summary>Disk raporu + geçici dosya temizliği.</summary>
        private void RunDiskReport()
        {
            GameMaintenanceService.DiskReport rep = GameMaintenanceService.BuildDiskReport(_allProfiles);

            MessageBoxResult res = MessageBox.Show(
                rep.Text + "\n\nGeçici indirme artıkları (yarım kalan paketler) temizlensin mi?",
                "Titan // Disk Raporu", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (res == MessageBoxResult.Yes)
            {
                long freed = GameMaintenanceService.CleanupAllTemp();
                _txtBottomStatus.Text = string.Format("🧹 Geçici dosyalar temizlendi: {0:0.0} MB", freed / 1048576.0);
                MessageBox.Show(string.Format("{0:0.0} MB boşaltıldı.", freed / 1048576.0),
                    "Temizlik", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>İndirme hız sınırını döngüsel değiştirir.</summary>
        private void CycleDownloadSpeedLimit()
        {
            long[] steps = new long[] { 0, 5, 10, 25, 50 };
            long current = DownloadInstallService.SpeedLimitBytesPerSec;
            long next = 0;

            for (int i = 0; i < steps.Length; i++)
            {
                if (ConvertMb(steps[i]) == current) { next = ConvertMb(steps[(i + 1) % steps.Length]); break; }
            }
            if (next == 0 && current != 0) next = 0;

            DownloadInstallService.SpeedLimitBytesPerSec = next;
            _txtBottomStatus.Text = next == 0 ? "🚀 İndirme hızı: SINIRSIZ" :
                string.Format("⏱ İndirme hızı: {0:0} MB/sn", next / 1048576.0);
        }

        private static long ConvertMb(long mb) { return mb <= 0 ? 0 : mb * 1048576L; }

        /// <summary>Katalog için magnet taraması (403 hostlarda torrent'e düşmek için).</summary>
        private void RunMagnetScan(int limit)
        {
            _txtBottomStatus.Text = "🧲 Oyun sayfalarında magnet/.torrent aranıyor...";

            ThreadPool.QueueUserWorkItem(delegate
            {
                int found = 0;
                try
                {
                    found = MagnetCatalog.BuildForCatalog(_allProfiles, limit, delegate(string m, int pct)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (_txtBottomStatus != null) _txtBottomStatus.Text = "🧲 " + m;
                        }));
                    });
                }
                catch { }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _txtBottomStatus.Text = string.Format("🧲 Magnet taraması bitti: {0} yeni magnet • toplam {1}", found, MagnetCatalog.Count());
                    Win32.PlayTechChirp(found > 0);
                    MessageBox.Show(string.Format("{0} yeni magnet bulundu.\nToplam magnet kaydı: {1}\n\nBu oyunlarda 403 olsa bile torrent ile indirme otomatik devreye girer.",
                            found, MagnetCatalog.Count()),
                        "Titan // Magnet Kataloğu", MessageBoxButton.OK, MessageBoxImage.Information);
                }));
            });
        }

        /// <summary>Zamanlanmış indirme: N dakika sonra başlat.</summary>
        private void RunScheduledDownload()
        {
            if (_selectedProfile == null)
            {
                MessageBox.Show("Önce bir oyun seçin.", "Zamanlayıcı", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string input = TextPromptDialog.Prompt(this, "Titan // Zamanlanmış İndirme",
                "Kaç dakika sonra başlasın? (ör. 30)", "30");
            if (string.IsNullOrWhiteSpace(input)) return;

            int minutes;
            if (!int.TryParse(input.Trim(), out minutes) || minutes <= 0 || minutes > 1440)
            {
                MessageBox.Show("1 ile 1440 arasında bir dakika girin.", "Zamanlayıcı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            GameProfile gp = _selectedProfile;
            _txtBottomStatus.Text = string.Format("🕒 '{0}' indirmesi {1} dakika sonra başlayacak.", gp.Name, minutes);

            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(minutes * 60 * 1000);
                try
                {
                    DownloadInstallService.SetTargetInstallDirectoryForGame(gp, DownloadInstallService.GetTargetInstallDirectoryForGame(gp));
                    _downloader.StartInAppAutomatedPipeline(gp);
                }
                catch { }
            });
        }
    }
}
