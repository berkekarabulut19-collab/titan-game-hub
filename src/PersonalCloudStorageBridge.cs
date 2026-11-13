using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace TitanGameHub
{
    public class CloudStorageProviderInfo
    {
        public string ProviderName { get; set; }
        public string CloudDirectoryPath { get; set; }
        public bool IsDetected { get; set; }
        public int SyncedSaveCount { get; set; }
    }

    /// <summary>
    /// Personal Zero-Cost Cloud Save Storage Bridge:
    /// Automatically connects to user's existing Google Drive, OneDrive, or Dropbox folders.
    /// Encrypts co-op save files using AES-256 and syncs them to the user's private cloud,
    /// providing permanent, format-proof backup with zero subscription cost or server reliance.
    /// </summary>
    public static class PersonalCloudStorageBridge
    {
        private static readonly byte[] AesSalt = new byte[] { 0x48, 0x75, 0x62, 0x54, 0x69, 0x74, 0x61, 0x6E };

        public static List<CloudStorageProviderInfo> DetectLocalCloudProviders()
        {
            List<CloudStorageProviderInfo> list = new List<CloudStorageProviderInfo>();
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // 1. Google Drive
            string gDrive = Path.Combine(userProfile, "Google Drive", "My Drive");
            string gDriveAlt = "G:\\My Drive";
            string gPath = Directory.Exists(gDrive) ? gDrive : (Directory.Exists(gDriveAlt) ? gDriveAlt : null);
            list.Add(new CloudStorageProviderInfo
            {
                ProviderName = "Google Drive",
                CloudDirectoryPath = gPath,
                IsDetected = !string.IsNullOrEmpty(gPath)
            });

            // 2. Microsoft OneDrive
            string oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            list.Add(new CloudStorageProviderInfo
            {
                ProviderName = "Microsoft OneDrive",
                CloudDirectoryPath = oneDrive,
                IsDetected = !string.IsNullOrEmpty(oneDrive) && Directory.Exists(oneDrive)
            });

            // 3. Dropbox
            string dropbox = Path.Combine(userProfile, "Dropbox");
            list.Add(new CloudStorageProviderInfo
            {
                ProviderName = "Dropbox",
                CloudDirectoryPath = dropbox,
                IsDetected = Directory.Exists(dropbox)
            });

            return list;
        }

        public static bool SyncSaveToCloud(GameProfile profile, string cloudRootDir, out string message)
        {
            if (profile == null)
            {
                message = "Oyun profili geçersiz.";
                return false;
            }

            List<string> savList = GvasSaveEditorService.LocateSaveFilesForGame(profile);
            if (savList.Count == 0)
            {
                message = "Yedeklenecek yerel save dosyası tespit edilemedi.";
                return false;
            }

            try
            {
                if (string.IsNullOrEmpty(cloudRootDir) || !Directory.Exists(cloudRootDir))
                {
                    List<CloudStorageProviderInfo> detected = DetectLocalCloudProviders();
                    for (int i = 0; i < detected.Count; i++)
                    {
                        if (detected[i].IsDetected)
                        {
                            cloudRootDir = detected[i].CloudDirectoryPath;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(cloudRootDir) || !Directory.Exists(cloudRootDir))
                {
                    cloudRootDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TitanCloudSaves");
                    if (!Directory.Exists(cloudRootDir)) Directory.CreateDirectory(cloudRootDir);
                }

                string targetGameDir = Path.Combine(cloudRootDir, "TitanSaves", profile.Id);
                if (!Directory.Exists(targetGameDir)) Directory.CreateDirectory(targetGameDir);

                // Create compressed zip archive
                byte[] rawZip;
                using (MemoryStream ms = new MemoryStream())
                {
                    using (ZipArchive arch = new ZipArchive(ms, ZipArchiveMode.Create, true))
                    {
                        for (int i = 0; i < savList.Count; i++)
                        {
                            string f = savList[i];
                            ZipArchiveEntry ent = arch.CreateEntry(Path.GetFileName(f), CompressionLevel.Optimal);
                            using (Stream es = ent.Open())
                            using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                fs.CopyTo(es);
                            }
                        }
                    }
                    rawZip = ms.ToArray();
                }

                // AES-256 Encryption
                byte[] encrypted = EncryptBytesAes256(rawZip, profile.Id + "_TitanSecretKey_2026");
                string outPath = Path.Combine(targetGameDir, string.Format("save_vault_{0:yyyyMMdd_HHmmss}.tenc", DateTime.Now));
                File.WriteAllBytes(outPath, encrypted);

                message = string.Format("✅ AES-256 Şifreli Save Bulut Kasanıza Senkronize Edildi!\n\nKonum: {0}\nDosya: {1} ({2:0.0} KB)",
                    targetGameDir, Path.GetFileName(outPath), encrypted.Length / 1024.0);
                return true;
            }
            catch (Exception ex)
            {
                message = "Bulut save senkronizasyon hatası: " + ex.Message;
                return false;
            }
        }

        private static byte[] EncryptBytesAes256(byte[] data, string password)
        {
            using (Rfc2898DeriveBytes keyDerive = new Rfc2898DeriveBytes(password, AesSalt, 1000))
            using (AesManaged aes = new AesManaged())
            {
                aes.Key = keyDerive.GetBytes(32);
                aes.IV = keyDerive.GetBytes(16);

                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(data, 0, data.Length);
                        cs.FlushFinalBlock();
                    }
                    return ms.ToArray();
                }
            }
        }
    }
}
