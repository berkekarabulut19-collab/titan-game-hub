using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TitanGameHub
{
    public class SaveBackupInfo
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public DateTime Timestamp { get; set; }
        public string FormattedSize { get; set; }
    }

    public static class SaveSyncService
    {
        private static readonly byte[] EncryptionSalt = new byte[] { 0x54, 0x49, 0x54, 0x41, 0x4E, 0x5F, 0x53, 0x41, 0x56, 0x45, 0x5F, 0x56, 0x41, 0x55, 0x4C, 0x54 };

        public static string GetBackupsRoot()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }
            return dir;
        }

        public static string LocateGameSaveDirectory(GameProfile gp)
        {
            if (gp == null) return null;
            string cleanKey = Regex.Replace(gp.Name ?? "", @"[^a-zA-Z0-9]", "").ToLowerInvariant();
            if (cleanKey.Length > 7) cleanKey = cleanKey.Substring(0, 7);

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string appId = string.IsNullOrEmpty(gp.SteamAppId) ? "480" : gp.SteamAppId;

            // 1. Check Goldberg & SteamEmu Save Vaults (AppID based)
            string goldbergDir = Path.Combine(userProfile, "AppData", "Roaming", "Goldberg SteamEmu Saves", appId);
            if (Directory.Exists(goldbergDir)) return goldbergDir;

            string codexDir = Path.Combine(userProfile, "AppData", "Roaming", "Steam", "CODEX", appId);
            if (Directory.Exists(codexDir)) return codexDir;

            string[] roots = new string[]
            {
                Path.Combine(userProfile, "AppData", "LocalLow"),
                Path.Combine(userProfile, "AppData", "Local"),
                Path.Combine(userProfile, "AppData", "Roaming"),
                Path.Combine(userProfile, "Saved Games"),
                Path.Combine(myDocuments, "My Games"),
                myDocuments
            };

            foreach (string r in roots)
            {
                if (!Directory.Exists(r)) continue;
                try
                {
                    string[] topDirs = Directory.GetDirectories(r);
                    foreach (string td in topDirs)
                    {
                        string tName = Regex.Replace(Path.GetFileName(td), @"[^a-zA-Z0-9]", "").ToLowerInvariant();
                        if (!string.IsNullOrEmpty(cleanKey) && tName.Contains(cleanKey))
                            return td;

                        try
                        {
                            string[] subDirs = Directory.GetDirectories(td);
                            foreach (string sd in subDirs)
                            {
                                string sName = Regex.Replace(Path.GetFileName(sd), @"[^a-zA-Z0-9]", "").ToLowerInvariant();
                                if (!string.IsNullOrEmpty(cleanKey) && sName.Contains(cleanKey))
                                    return sd;
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // Fallback to game working directory or managed save directory
            if (!string.IsNullOrEmpty(gp.WorkingDirectory) && Directory.Exists(gp.WorkingDirectory))
            {
                string localSave = Path.Combine(gp.WorkingDirectory, "Saves");
                if (!Directory.Exists(localSave))
                {
                    try
                    {
                        Directory.CreateDirectory(localSave);
                        File.WriteAllText(Path.Combine(localSave, "save_meta.ini"),
                            "[TitanSaveSync]\r\nGame=" + gp.Name + "\r\nAppId=" + gp.SteamAppId + "\r\nTimestamp=" + DateTime.Now.ToString("O"));
                    }
                    catch { }
                }
                return localSave;
            }

            string managedSave = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Saves", Regex.Replace(gp.Name, @"[^\w\s-]", "").Trim());
            if (!Directory.Exists(managedSave))
            {
                try
                {
                    Directory.CreateDirectory(managedSave);
                    File.WriteAllText(Path.Combine(managedSave, "save_meta.ini"),
                        "[TitanSaveSync]\r\nGame=" + gp.Name + "\r\nAppId=" + gp.SteamAppId + "\r\nTimestamp=" + DateTime.Now.ToString("O"));
                }
                catch { }
            }
            return managedSave;
        }

        public static string CreateSaveBackup(GameProfile gp, out string statusMsg)
        {
            statusMsg = "";
            try
            {
                string saveDir = LocateGameSaveDirectory(gp);
                if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir))
                {
                    statusMsg = "Save dizini bulunamadi.";
                    return null;
                }

                string safeName = Regex.Replace(gp.Name, @"[^\w\s-]", "").Trim().Replace(" ", "_");
                string zipName = string.Format("{0}_Save_{1}.zip", safeName, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                string zipPath = Path.Combine(GetBackupsRoot(), zipName);

                if (File.Exists(zipPath)) File.Delete(zipPath);

                string tempStage = Path.Combine(Path.GetTempPath(), "TitanSaveBackup_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Directory.CreateDirectory(tempStage);

                foreach (string f in Directory.GetFiles(saveDir, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(saveDir.Length).TrimStart('\\', '/');
                    string target = Path.Combine(tempStage, rel);
                    string targetFolder = Path.GetDirectoryName(target);
                    if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);
                    File.Copy(f, target, true);
                }

                string manifest = string.Format(
                    "Game={0}\r\nAppId={1}\r\nTimestamp={2}\r\nSourcePath={3}\r\nBackupEngine=TitanSaveSync_v7\r\n",
                    gp.Name, gp.SteamAppId, DateTime.Now.ToString("O"), saveDir);
                File.WriteAllText(Path.Combine(tempStage, "backup_manifest.ini"), manifest, Encoding.UTF8);

                ZipFile.CreateFromDirectory(tempStage, zipPath);
                try { Directory.Delete(tempStage, true); } catch { }

                statusMsg = gp.Name + " Save yedegi olusturuldu: " + zipName;
                return zipPath;
            }
            catch (Exception ex)
            {
                statusMsg = "Save yedekleme hatasi: " + ex.Message;
                return null;
            }
        }

        public static bool RestoreLatestSaveBackup(GameProfile gp, out string statusMsg)
        {
            statusMsg = "";
            try
            {
                string safeName = Regex.Replace(gp.Name, @"[^\w\s-]", "").Trim().Replace(" ", "_");
                string[] files = Directory.GetFiles(GetBackupsRoot(), safeName + "_Save_*.zip");
                if (files.Length == 0)
                {
                    statusMsg = gp.Name + " icin henuz alinmis bir Save yedegi bulunamadi.";
                    return false;
                }

                Array.Sort(files);
                string latestZip = files[files.Length - 1];
                return RestoreSpecificBackup(gp, latestZip, out statusMsg);
            }
            catch (Exception ex)
            {
                statusMsg = "Save geri yukleme hatasi: " + ex.Message;
                return false;
            }
        }

        public static bool RestoreSpecificBackup(GameProfile gp, string zipPath, out string statusMsg)
        {
            statusMsg = "";
            try
            {
                if (!File.Exists(zipPath))
                {
                    statusMsg = "Yedek zip dosyasi bulunamadi: " + zipPath;
                    return false;
                }

                string saveDir = LocateGameSaveDirectory(gp);
                if (string.IsNullOrEmpty(saveDir))
                {
                    statusMsg = "Hedef save dizini tespit edilemedi.";
                    return false;
                }

                string tempExtract = Path.Combine(Path.GetTempPath(), "TitanSaveRestore_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Directory.CreateDirectory(tempExtract);
                ZipFile.ExtractToDirectory(zipPath, tempExtract);

                foreach (string file in Directory.GetFiles(tempExtract, "*", SearchOption.AllDirectories))
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.Equals("backup_manifest.ini", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string rel = file.Substring(tempExtract.Length).TrimStart('\\', '/');
                    string dest = Path.Combine(saveDir, rel);
                    string destFolder = Path.GetDirectoryName(dest);
                    if (!Directory.Exists(destFolder)) Directory.CreateDirectory(destFolder);
                    File.Copy(file, dest, true);
                }
                Directory.Delete(tempExtract, true);

                statusMsg = gp.Name + " Save yedegi (" + Path.GetFileName(zipPath) + ") basariyla geri yuklendi!";
                return true;
            }
            catch (Exception ex)
            {
                statusMsg = "Save geri yukleme hatasi: " + ex.Message;
                return false;
            }
        }

        public static List<SaveBackupInfo> GetBackupsList(GameProfile gp)
        {
            List<SaveBackupInfo> list = new List<SaveBackupInfo>();
            if (gp == null) return list;

            try
            {
                string safeName = Regex.Replace(gp.Name, @"[^\w\s-]", "").Trim().Replace(" ", "_");
                string[] files = Directory.GetFiles(GetBackupsRoot(), safeName + "_Save_*.zip");
                Array.Sort(files);
                Array.Reverse(files);

                foreach (string f in files)
                {
                    FileInfo fi = new FileInfo(f);
                    double kb = fi.Length / 1024.0;
                    string sizeStr = kb >= 1024.0 ? (kb / 1024.0).ToString("0.0") + " MB" : kb.ToString("0.0") + " KB";

                    SaveBackupInfo info = new SaveBackupInfo
                    {
                        FileName = Path.GetFileName(f),
                        FullPath = f,
                        Timestamp = fi.LastWriteTime,
                        FormattedSize = sizeStr
                    };
                    list.Add(info);
                }
            }
            catch { }
            return list;
        }

        public static int GetBackupCount(GameProfile gp)
        {
            if (gp == null) return 0;
            try
            {
                string safeName = Regex.Replace(gp.Name, @"[^\w\s-]", "").Trim().Replace(" ", "_");
                int saves = Directory.GetFiles(GetBackupsRoot(), safeName + "_Save_*.zip").Length;
                int snaps = Directory.GetFiles(GetBackupsRoot(), safeName + "_Snapshot_*.zip").Length;
                return saves + snaps;
            }
            catch { return 0; }
        }

        // ==========================================
        // SAVE ZAMAN TÜNELİ (MULTI-SLOT TIME MACHINE)
        // ==========================================
        public static List<SaveBackupInfo> GetGameSnapshots(GameProfile gp)
        {
            List<SaveBackupInfo> list = new List<SaveBackupInfo>();
            if (gp == null) return list;

            try
            {
                string safeName = Regex.Replace(gp.Name, @"[^\w\s-]", "").Trim().Replace(" ", "_");
                List<string> allFiles = new List<string>();
                allFiles.AddRange(Directory.GetFiles(GetBackupsRoot(), safeName + "_Snapshot_*.zip"));
                allFiles.AddRange(Directory.GetFiles(GetBackupsRoot(), safeName + "_Save_*.zip"));

                allFiles.Sort();
                allFiles.Reverse(); // Newest first

                int slot = 1;
                foreach (string f in allFiles)
                {
                    FileInfo fi = new FileInfo(f);
                    double kb = fi.Length / 1024.0;
                    string sizeStr = kb >= 1024.0 ? (kb / 1024.0).ToString("0.0") + " MB" : kb.ToString("0.0") + " KB";

                    string baseName = Path.GetFileNameWithoutExtension(f);
                    string tag = baseName.Contains("Snapshot") ? ("Slot #" + slot + " (Zaman Tüneli)") : ("Yedek #" + slot);

                    SaveBackupInfo info = new SaveBackupInfo
                    {
                        FileName = tag + " • " + fi.LastWriteTime.ToString("dd.MM.yyyy HH:mm:ss"),
                        FullPath = f,
                        Timestamp = fi.LastWriteTime,
                        FormattedSize = sizeStr
                    };
                    list.Add(info);
                    slot++;
                }
            }
            catch { }
            return list;
        }

        public static bool CreateSessionSnapshot(GameProfile gp, string note, out string statusMsg)
        {
            statusMsg = "";
            try
            {
                string saveDir = LocateGameSaveDirectory(gp);
                if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir))
                {
                    statusMsg = "Save dizini bulunamadı.";
                    return false;
                }

                string safeName = Regex.Replace(gp.Name, @"[^\w\s-]", "").Trim().Replace(" ", "_");
                string zipName = string.Format("{0}_Snapshot_{1}.zip", safeName, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                string zipPath = Path.Combine(GetBackupsRoot(), zipName);

                string tempStage = Path.Combine(Path.GetTempPath(), "TitanSnapshot_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Directory.CreateDirectory(tempStage);

                foreach (string f in Directory.GetFiles(saveDir, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(saveDir.Length).TrimStart('\\', '/');
                    string target = Path.Combine(tempStage, rel);
                    string targetFolder = Path.GetDirectoryName(target);
                    if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);
                    File.Copy(f, target, true);
                }

                string manifest = string.Format("Game={0}\r\nTimestamp={1}\r\nNote={2}\r\nType=TimeMachineSnapshot\r\n",
                    gp.Name, DateTime.Now.ToString("O"), note ?? "Otomatik Oturum Sonu");
                File.WriteAllText(Path.Combine(tempStage, "snapshot_meta.ini"), manifest, Encoding.UTF8);

                ZipFile.CreateFromDirectory(tempStage, zipPath);
                try { Directory.Delete(tempStage, true); } catch { }

                // Prune: keep last 10 snapshots for this game
                PruneOlderSnapshots(safeName, 10);

                statusMsg = string.Format("Zaman Tüneli: {0} için yeni save anlık görüntüsü alındı ({1})", gp.Name, zipName);
                return true;
            }
            catch (Exception ex)
            {
                statusMsg = "Snapshot hatası: " + ex.Message;
                return false;
            }
        }

        private static void PruneOlderSnapshots(string safeName, int maxKeep)
        {
            try
            {
                string[] files = Directory.GetFiles(GetBackupsRoot(), safeName + "_Snapshot_*.zip");
                if (files.Length > maxKeep)
                {
                    Array.Sort(files); // Oldest first
                    int toDelete = files.Length - maxKeep;
                    for (int i = 0; i < toDelete; i++)
                    {
                        try { File.Delete(files[i]); } catch { }
                    }
                }
            }
            catch { }
        }

        // ==========================================
        // AES-256 ENCRYPTED CLOUD VAULT
        // ==========================================
        public static bool EncryptBackup(string zipPath, string password, string outEncryptedPath)
        {
            try
            {
                if (!File.Exists(zipPath) || string.IsNullOrEmpty(password)) return false;

                Rfc2898DeriveBytes keyDerivation = new Rfc2898DeriveBytes(password, EncryptionSalt, 10000);
                byte[] key = keyDerivation.GetBytes(32); // 256-bit key
                byte[] iv = keyDerivation.GetBytes(16);  // 128-bit IV

                using (AesManaged aes = new AesManaged())
                {
                    aes.Key = key;
                    aes.IV = iv;
                    using (FileStream inFs = new FileStream(zipPath, FileMode.Open, FileAccess.Read))
                    using (FileStream outFs = new FileStream(outEncryptedPath, FileMode.Create, FileAccess.Write))
                    using (CryptoStream cs = new CryptoStream(outFs, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        byte[] buffer = new byte[65536];
                        int read;
                        while ((read = inFs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cs.Write(buffer, 0, read);
                        }
                    }
                }
                return true;
            }
            catch { return false; }
        }

        public static bool DecryptBackup(string encPath, string password, string outZipPath)
        {
            try
            {
                if (!File.Exists(encPath) || string.IsNullOrEmpty(password)) return false;

                Rfc2898DeriveBytes keyDerivation = new Rfc2898DeriveBytes(password, EncryptionSalt, 10000);
                byte[] key = keyDerivation.GetBytes(32);
                byte[] iv = keyDerivation.GetBytes(16);

                using (AesManaged aes = new AesManaged())
                {
                    aes.Key = key;
                    aes.IV = iv;
                    using (FileStream inFs = new FileStream(encPath, FileMode.Open, FileAccess.Read))
                    using (FileStream outFs = new FileStream(outZipPath, FileMode.Create, FileAccess.Write))
                    using (CryptoStream cs = new CryptoStream(inFs, aes.CreateDecryptor(), CryptoStreamMode.Read))
                    {
                        byte[] buffer = new byte[65536];
                        int read;
                        while ((read = cs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            outFs.Write(buffer, 0, read);
                        }
                    }
                }
                return true;
            }
            catch { return false; }
        }
    }
}
