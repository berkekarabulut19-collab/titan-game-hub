using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TitanGameHub
{
    /// <summary>
    /// Kütüphane bakımı: oyun kaldırma, artık dosya temizliği ve disk raporu.
    /// Steam'in "Uninstall" + "Disk kullanımı" karşılığı. Silme işlemleri SADECE
    /// oyunun kendi klasörü ve uygulamanın geçici dosyalarıyla sınırlıdır.
    /// </summary>
    public static class GameMaintenanceService
    {
        public class DiskReport
        {
            public long GamesBytes;
            public long TempBytes;
            public long BackupsBytes;
            public long CoversBytes;
            public long FreeBytes;
            public long TotalBytes;
            public int GameCount;
            public List<KeyValuePair<string, long>> TopGames = new List<KeyValuePair<string, long>>();
            public string Text = "";
        }

        /// <summary>Oyunu kaldırır: klasörü siler, katalog kaydını temizler.</summary>
        public static bool Uninstall(GameProfile gp, bool deleteFiles, out string message)
        {
            message = "";
            if (gp == null) { message = "Oyun seçilmedi."; return false; }

            try
            {
                string workDir = !string.IsNullOrEmpty(gp.WorkingDirectory)
                    ? gp.WorkingDirectory
                    : (!string.IsNullOrEmpty(gp.ExecutablePath) ? Path.GetDirectoryName(gp.ExecutablePath) : "");

                if (deleteFiles && !string.IsNullOrEmpty(workDir) && Directory.Exists(workDir))
                {
                    // Güvenlik: yalnızca bilinen kütüphane kökleri altındaysa sil
                    string target = Path.GetFullPath(workDir).TrimEnd('\\');
                    string matchedRoot = null;
                    List<string> allowedRoots = new List<string>(GamePathResolver.GetLibraryRoots(false));
                    string effRoot = DownloadInstallService.GetEffectiveGamesRoot();
                    if (!string.IsNullOrEmpty(effRoot)) allowedRoots.Add(effRoot);

                    foreach (string root in allowedRoots)
                    {
                        if (string.IsNullOrEmpty(root)) continue;
                        string r = Path.GetFullPath(root).TrimEnd('\\');
                        if ((target + "\\").StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(target, r, StringComparison.OrdinalIgnoreCase))
                        {
                            if (matchedRoot == null || r.Length > matchedRoot.Length)
                                matchedRoot = r;
                        }
                    }
                    if (string.IsNullOrEmpty(matchedRoot))
                    {
                        message = "Güvenlik: bu klasör bir oyun kütüphanesi kökünde değil, silinmedi (" + target + ").";
                        return false;
                    }

                    // Eğer ExecutablePath/WorkingDirectory alt klasördeyse (ör. D:\Games\Oyun\Binaries\Win64),
                    // kök altındaki 1. seviye oyun klasörünü (D:\Games\Oyun) bul ki geride tek dosya bile kalmasın!
                    string deleteTarget = target;
                    try
                    {
                        string rel = target.Substring(matchedRoot.Length).TrimStart('\\');
                        int slashIdx = rel.IndexOf('\\');
                        if (slashIdx > 0)
                        {
                            string topGameFolder = Path.Combine(matchedRoot, rel.Substring(0, slashIdx));
                            string topName = Path.GetFileName(topGameFolder).ToLowerInvariant();
                            if (Directory.Exists(topGameFolder) &&
                                topName != "games" && topName != "common" && topName != "steamapps" &&
                                topName != "_incoming" && !topName.StartsWith("_staging"))
                            {
                                deleteTarget = topGameFolder;
                            }
                        }
                    }
                    catch { }

                    ForceDeleteDirectoryCompletely(deleteTarget);
                    message = "Oyun ve tüm dosyaları diskten sıfır kalıntıyla silindi: " + deleteTarget;
                }
                else
                {
                    message = "Oyun kurulumu temizlendi (katalogda indirmeye hazır).";
                }

                if (deleteFiles)
                {
                    try { string stMsg; SteamIntegrationService.RemoveGameFromSteamShortcuts(gp, out stMsg); } catch { }
                }

                CleanupGameArtifacts(gp);
                RecordActivity("KALDIRILDI", gp.Name, gp.FileSize);

                string normTarget = GameDeduplicationEngine.NormalizeGameTitle(gp.Name);
                List<GameProfile> list = ProfileStore.LoadProfiles();
                bool isPureLocalOnly = !string.IsNullOrEmpty(gp.Id) &&
                                       gp.Id.StartsWith("local_", StringComparison.OrdinalIgnoreCase) &&
                                       string.IsNullOrEmpty(gp.DownloadUrl) &&
                                       string.IsNullOrEmpty(gp.DirectMirrorUrl);
                if (isPureLocalOnly)
                {
                    list.RemoveAll(delegate(GameProfile p) { return p != null && p.Id == gp.Id; });
                }
                else
                {
                    foreach (GameProfile p in list)
                    {
                        if (p == null) continue;
                        if (p.Id == gp.Id || (!string.IsNullOrEmpty(normTarget) && GameDeduplicationEngine.NormalizeGameTitle(p.Name) == normTarget))
                        {
                            p.ExecutablePath = "";
                            p.WorkingDirectory = "";
                        }
                    }
                }
                ProfileStore.SaveProfiles(list);

                return true;
            }
            catch (Exception ex)
            {
                message = "Kaldırma hatası: " + ex.Message;
                return false;
            }
        }

        private static void ForceDeleteDirectoryCompletely(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
            }
            List<string> subDirs = new List<string>();
            foreach (string d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))
            {
                subDirs.Add(d);
                try { File.SetAttributes(d, FileAttributes.Normal); } catch { }
            }
            try { File.SetAttributes(dir, FileAttributes.Normal); } catch { }

            try
            {
                Directory.Delete(dir, true);
                return;
            }
            catch { }

            foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { }
            }
            subDirs.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
            foreach (string d in subDirs)
            {
                try { File.SetAttributes(d, FileAttributes.Normal); Directory.Delete(d, true); } catch { }
            }
            try { Directory.Delete(dir, true); } catch { }
        }

        /// <summary>Oyunun geçici indirme dosyalarını (<oyun>_pkg*, _parts) siler.</summary>
        public static long CleanupGameArtifacts(GameProfile gp)
        {
            long freed = 0;
            try
            {
                if (gp == null) return 0;
                string root = DownloadInstallService.GetEffectiveGamesRoot();
                string safe = Regex.Replace(gp.Name ?? "game", @"[^\w\s-]", "").Trim();

                foreach (string pattern in new string[] { safe + "_pkg*", safe + "_parts", safe + "_parts*" })
                {
                    try
                    {
                        foreach (string f in Directory.GetFiles(root, pattern, SearchOption.TopDirectoryOnly))
                        {
                            long len = new FileInfo(f).Length;
                            File.Delete(f);
                            freed += len;
                        }
                        foreach (string d in Directory.GetDirectories(root, pattern, SearchOption.TopDirectoryOnly))
                        {
                            freed += GetDirSize(d);
                            ForceDeleteDirectoryCompletely(d);
                        }
                    }
                    catch { }
                }

                // _incoming altındaki geçici indirilen arşivleri de temizle
                try
                {
                    string incDir = Path.Combine(root, "_incoming");
                    if (Directory.Exists(incDir) && !string.IsNullOrEmpty(safe) && safe.Length >= 3)
                    {
                        foreach (string f in Directory.EnumerateFiles(incDir, "*" + safe + "*", SearchOption.TopDirectoryOnly))
                        {
                            try { freed += new FileInfo(f).Length; File.Delete(f); } catch { }
                        }
                    }
                }
                catch { }

                // Masaüstü ve Başlat Menüsü kısayollarını (.url / .lnk) da kaldır
                try
                {
                    string rawName = gp.Name ?? "";
                    string cleanSc = rawName;
                    foreach (char c in Path.GetInvalidFileNameChars())
                        cleanSc = cleanSc.Replace(c, '_');
                    cleanSc = cleanSc.Trim();

                    string[] dirs = new string[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Titan Game Hub")
                    };
                    string[] exts = new string[] { ".url", ".lnk" };
                    foreach (string d in dirs)
                    {
                        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) continue;
                        foreach (string baseN in new string[] { cleanSc, safe })
                        {
                            if (string.IsNullOrEmpty(baseN)) continue;
                            foreach (string ext in exts)
                            {
                                string p = Path.Combine(d, baseN + ext);
                                try { if (File.Exists(p)) File.Delete(p); } catch { }
                            }
                        }
                    }
                }
                catch { }
            }
            catch { }
            return freed;
        }

        /// <summary>Tüm geçici indirme artıkları (_incoming, _torrent*, _pkg, .resume).</summary>
        public static long CleanupAllTemp()
        {
            long freed = 0;
            try
            {
                string root = DownloadInstallService.GetEffectiveGamesRoot();
                if (!Directory.Exists(root)) return 0;

                foreach (string f in Directory.GetFiles(root, "*.resume", SearchOption.TopDirectoryOnly))
                {
                    try { freed += new FileInfo(f).Length; File.Delete(f); } catch { }
                }
                foreach (string f in Directory.GetFiles(root, "*_pkg.*", SearchOption.TopDirectoryOnly))
                {
                    try { freed += new FileInfo(f).Length; File.Delete(f); } catch { }
                }
                foreach (string d in Directory.GetDirectories(root, "_incoming", SearchOption.TopDirectoryOnly)) freed += DeleteDir(d);
                foreach (string d in Directory.GetDirectories(root, "_torrent*", SearchOption.TopDirectoryOnly)) freed += DeleteDir(d);
                foreach (string d in Directory.GetDirectories(root, "*_parts", SearchOption.TopDirectoryOnly)) freed += DeleteDir(d);
            }
            catch { }
            return freed;
        }

        private static long DeleteDir(string dir)
        {
            long size = GetDirSize(dir);
            try { Directory.Delete(dir, true); } catch { }
            return size;
        }

        public static long GetDirSize(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return 0;
                long total = 0;
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
                return total;
            }
            catch { return 0; }
        }

        /// <summary>Disk kullanım raporu: oyunlar, geçici dosyalar, yedekler, kapaklar.</summary>
        public static DiskReport BuildDiskReport(List<GameProfile> catalog)
        {
            DiskReport r = new DiskReport();
            try
            {
                string root = DownloadInstallService.GetEffectiveGamesRoot();
                r.GamesBytes = GetDirSize(root);

                long free, total;
                GetDiskInfo(root, out free, out total);
                r.FreeBytes = free;
                r.TotalBytes = total;

                List<KeyValuePair<string, long>> sizes = new List<KeyValuePair<string, long>>();
                if (catalog != null)
                {
                    foreach (GameProfile gp in catalog)
                    {
                        if (gp == null || !gp.IsInstalled) continue;
                        r.GameCount++;
                        long s = GetDirSize(gp.WorkingDirectory);
                        if (s > 0) sizes.Add(new KeyValuePair<string, long>(gp.Name, s));
                    }
                }
                sizes.Sort(delegate(KeyValuePair<string, long> a, KeyValuePair<string, long> b) { return b.Value.CompareTo(a.Value); });
                for (int i = 0; i < sizes.Count && i < 8; i++) r.TopGames.Add(sizes[i]);

                r.BackupsBytes = GetDirSize(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups"));
                r.CoversBytes = GetDirSize(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache"));

                long temp = 0;
                try
                {
                    foreach (string f in Directory.GetFiles(root, "*.resume", SearchOption.TopDirectoryOnly)) temp += new FileInfo(f).Length;
                    foreach (string f in Directory.GetFiles(root, "*_pkg.*", SearchOption.TopDirectoryOnly)) temp += new FileInfo(f).Length;
                    temp += GetDirSize(Path.Combine(root, "_incoming"));
                }
                catch { }
                r.TempBytes = temp;

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("DİSK KULLANIMI");
                sb.AppendLine(new string('-', 42));
                sb.AppendLine(string.Format("Kütüphane kökü : {0}", root));
                sb.AppendLine(string.Format("Oyunlar        : {0:0.0} GB ({1} oyun)", r.GamesBytes / 1073741824.0, r.GameCount));
                sb.AppendLine(string.Format("Geçici indirme : {0:0.0} GB", r.TempBytes / 1073741824.0));
                sb.AppendLine(string.Format("Kayıt yedekleri: {0:0.0} MB", r.BackupsBytes / 1048576.0));
                sb.AppendLine(string.Format("Kapak önbelleği: {0:0.0} MB", r.CoversBytes / 1048576.0));
                if (r.FreeBytes > 0)
                    sb.AppendLine(string.Format("Boş alan       : {0:0.0} GB / {1:0.0} GB", r.FreeBytes / 1073741824.0, r.TotalBytes / 1073741824.0));
                sb.AppendLine();
                if (r.TopGames.Count > 0)
                {
                    sb.AppendLine("En büyük oyunlar:");
                    foreach (KeyValuePair<string, long> kv in r.TopGames)
                        sb.AppendLine(string.Format("  • {0,-28} {1:0.00} GB", kv.Key, kv.Value / 1073741824.0));
                }
                r.Text = sb.ToString();
            }
            catch (Exception ex) { r.Text = "Rapor hatası: " + ex.Message; }
            return r;
        }

        private static void GetDiskInfo(string path, out long free, out long total)
        {
            free = 0; total = 0;
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root)) return;
                DriveInfo di = new DriveInfo(root);
                if (!di.IsReady) return;
                free = di.AvailableFreeSpace;
                total = di.TotalSize;
            }
            catch { }
        }

        private static readonly object ActivitySync = new object();

        public static void RecordActivity(string action, string gameName, string sizeText)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "activity_history.log");
                string dir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string entry = string.Format("[{0:yyyy-MM-dd HH:mm}] {1} | {2} | {3}",
                    DateTime.Now,
                    string.IsNullOrEmpty(action) ? "ISLEM" : action,
                    string.IsNullOrEmpty(gameName) ? "Oyun" : gameName,
                    string.IsNullOrEmpty(sizeText) ? "-" : sizeText);

                lock (ActivitySync)
                {
                    List<string> lines = File.Exists(logPath)
                        ? new List<string>(File.ReadAllLines(logPath, Encoding.UTF8))
                        : new List<string>();
                    lines.Insert(0, entry);
                    if (lines.Count > 200) lines.RemoveRange(200, lines.Count - 200);
                    File.WriteAllLines(logPath, lines.ToArray(), Encoding.UTF8);
                }
            }
            catch { }
        }

        public static List<string> GetRecentActivityLines(int max = 20)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "activity_history.log");
                if (!File.Exists(logPath)) return new List<string>();
                lock (ActivitySync)
                {
                    string[] all = File.ReadAllLines(logPath, Encoding.UTF8);
                    List<string> res = new List<string>();
                    for (int i = 0; i < all.Length && i < max; i++) res.Add(all[i]);
                    return res;
                }
            }
            catch { return new List<string>(); }
        }
    }
}
