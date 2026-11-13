using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TitanGameHub
{
    public class GvasHeaderInfo
    {
        public bool IsValidGvas { get; set; }
        public int SaveVersion { get; set; }
        public int PackageVersion { get; set; }
        public string EngineVersion { get; set; }
        public string SaveGameType { get; set; }
        public long FileSizeBytes { get; set; }
        public string FilePath { get; set; }
        public List<string> DetectedProperties { get; set; }
        public string DiagnosticSummary { get; set; }

        public GvasHeaderInfo()
        {
            DetectedProperties = new List<string>();
        }
    }

    /// <summary>
    /// Universal GVAS (Unreal Engine 4 & 5) & Unity Save Game Inspector & Auto-Repair Service:
    /// Locates save files for popular co-op games (Palworld, Sons of the Forest, Lethal Company, etc.),
    /// creates pre-flight backups, inspects binary GVAS headers, and recovers corrupt saves.
    /// </summary>
    public static class GvasSaveEditorService
    {
        private static readonly byte[] GvasMagic = new byte[] { 0x47, 0x56, 0x41, 0x53 }; // "GVAS"

        /// <summary>
        /// Locates all known save files for the given game across LocalAppData, LocalLow, and Goldberg paths.
        /// </summary>
        public static List<string> LocateSaveFilesForGame(GameProfile profile)
        {
            List<string> results = new List<string>();
            if (profile == null) return results;

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localLow = Path.Combine(userProfile, "AppData", "LocalLow");

            string safeName = profile.Name.Replace(" ", "").Replace(":", "").Replace("-", "");

            // 1. Unreal Engine standard paths: %LOCALAPPDATA%\<Game>\Saved\SaveGames\*.sav
            try
            {
                if (Directory.Exists(localAppData))
                {
                    string[] subDirs = Directory.GetDirectories(localAppData);
                    for (int i = 0; i < subDirs.Length; i++)
                    {
                        string dir = subDirs[i];
                        string dirName = Path.GetFileName(dir);
                        if (dirName.IndexOf(safeName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            safeName.IndexOf(dirName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string savedPath = Path.Combine(dir, "Saved", "SaveGames");
                            if (Directory.Exists(savedPath))
                            {
                                string[] savs = Directory.GetFiles(savedPath, "*.sav", SearchOption.AllDirectories);
                                results.AddRange(savs);
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. Unity standard paths: %USERPROFILE%\AppData\LocalLow\<Dev>\<Game>\*.dat, *.json, *.sav
            try
            {
                if (Directory.Exists(localLow))
                {
                    string[] companyDirs = Directory.GetDirectories(localLow);
                    for (int c = 0; c < companyDirs.Length; c++)
                    {
                        string cDir = companyDirs[c];
                        string[] gameDirs = Directory.GetDirectories(cDir);
                        for (int g = 0; g < gameDirs.Length; g++)
                        {
                            string gDir = gameDirs[g];
                            string gName = Path.GetFileName(gDir);
                            if (gName.IndexOf(safeName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                safeName.IndexOf(gName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                string[] uFiles = Directory.GetFiles(gDir, "*.*", SearchOption.TopDirectoryOnly);
                                for (int f = 0; f < uFiles.Length; f++)
                                {
                                    string ext = Path.GetExtension(uFiles[f]).ToLowerInvariant();
                                    if (ext == ".dat" || ext == ".json" || ext == ".sav" || ext == ".txt")
                                    {
                                        results.Add(uFiles[f]);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // 3. Goldberg Steam Emulator Saves: %APPDATA%\Goldberg SteamEmu Saves\<AppId>\remote\*.sav
            try
            {
                string appId = string.IsNullOrEmpty(profile.SteamAppId) ? "480" : profile.SteamAppId;
                string goldbergDir = Path.Combine(appData, "Goldberg SteamEmu Saves", appId, "remote");
                if (Directory.Exists(goldbergDir))
                {
                    string[] gSavs = Directory.GetFiles(goldbergDir, "*.*", SearchOption.AllDirectories);
                    results.AddRange(gSavs);
                }
            }
            catch { }

            // Deduplicate
            List<string> unique = new List<string>();
            for (int i = 0; i < results.Count; i++)
            {
                if (!unique.Contains(results[i]))
                    unique.Add(results[i]);
            }
            return unique;
        }

        /// <summary>
        /// Reads and inspects the binary GVAS / Unreal Engine save file header.
        /// </summary>
        public static GvasHeaderInfo InspectGvasSave(string filePath)
        {
            GvasHeaderInfo info = new GvasHeaderInfo();
            info.FilePath = filePath;

            if (!File.Exists(filePath))
            {
                info.DiagnosticSummary = "Save dosyası mevcut değil: " + filePath;
                return info;
            }

            try
            {
                FileInfo fi = new FileInfo(filePath);
                info.FileSizeBytes = fi.Length;

                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    if (fs.Length < 16)
                    {
                        info.DiagnosticSummary = "Dosya boyutu çok küçük (boş veya hasarlı save).";
                        return info;
                    }

                    byte[] magic = br.ReadBytes(4);
                    if (magic[0] == GvasMagic[0] && magic[1] == GvasMagic[1] &&
                        magic[2] == GvasMagic[2] && magic[3] == GvasMagic[3])
                    {
                        info.IsValidGvas = true;
                        info.SaveVersion = br.ReadInt32();
                        info.PackageVersion = br.ReadInt32();

                        ushort engineMajor = br.ReadUInt16();
                        ushort engineMinor = br.ReadUInt16();
                        ushort enginePatch = br.ReadUInt16();
                        uint engineBuild = br.ReadUInt32();
                        info.EngineVersion = string.Format("Unreal Engine {0}.{1}.{2} (Build {3})", engineMajor, engineMinor, enginePatch, engineBuild);

                        // Custom versions format & save class
                        int customVerFormat = br.ReadInt32();
                        int customVerCount = br.ReadInt32();
                        if (customVerCount > 0 && customVerCount < 500)
                        {
                            fs.Seek(customVerCount * 20, SeekOrigin.Current); // GUID (16 bytes) + Int32 (4 bytes)
                        }

                        // SaveGameType FString
                        info.SaveGameType = ReadFString(br);

                        // Scan for visible property strings in the first 8KB
                        byte[] previewBuf = new byte[Math.Min(8192, (int)fs.Length)];
                        fs.Seek(0, SeekOrigin.Begin);
                        int readBytes = fs.Read(previewBuf, 0, previewBuf.Length);

                        string textAscii = Encoding.ASCII.GetString(previewBuf, 0, readBytes);
                        string[] propKeywords = new string[] { "Player", "Level", "Health", "Gold", "Coins", "Exp", "Score", "Inventory", "Item", "World", "Slot", "Progression" };

                        for (int k = 0; k < propKeywords.Length; k++)
                        {
                            if (textAscii.IndexOf(propKeywords[k], StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                info.DetectedProperties.Add(propKeywords[k]);
                            }
                        }

                        info.DiagnosticSummary = string.Format("Geçerli UE GVAS Kaydı — Sürüm: {0}, Motor: {1}, Tür: {2}", info.SaveVersion, info.EngineVersion, string.IsNullOrEmpty(info.SaveGameType) ? "CustomSaveGame" : info.SaveGameType);
                    }
                    else
                    {
                        // Check if JSON / Plain text save (Unity / Godot / Custom)
                        fs.Seek(0, SeekOrigin.Begin);
                        byte[] firstBytes = br.ReadBytes(Math.Min(256, (int)fs.Length));
                        string preview = Encoding.UTF8.GetString(firstBytes);
                        if (preview.StartsWith("{") || preview.StartsWith("["))
                        {
                            info.IsValidGvas = false;
                            info.SaveGameType = "JSON / Metin Veri Kaydı";
                            info.DiagnosticSummary = "Unity / Genel JSON Save Dosyası (Doğrudan düzenlenebilir).";
                        }
                        else
                        {
                            info.IsValidGvas = false;
                            info.SaveGameType = "Özel İkili (Binary) Kayıt";
                            info.DiagnosticSummary = "Özel oyun formatı veya şifrelenmiş kayıt.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                info.DiagnosticSummary = "Save okuma hatası: " + ex.Message;
            }

            return info;
        }

        private static string ReadFString(BinaryReader br)
        {
            try
            {
                int len = br.ReadInt32();
                if (len == 0 || len > 4096 || len < -4096) return "";
                if (len > 0)
                {
                    byte[] strBytes = br.ReadBytes(len);
                    return Encoding.ASCII.GetString(strBytes).TrimEnd('\0');
                }
                else
                {
                    int charCount = -len;
                    byte[] strBytes = br.ReadBytes(charCount * 2);
                    return Encoding.Unicode.GetString(strBytes).TrimEnd('\0');
                }
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Creates an automated timestamped backup of the save file.
        /// </summary>
        public static string CreateBackup(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                string dir = Path.GetDirectoryName(filePath);
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                string ext = Path.GetExtension(filePath);
                string bakPath = Path.Combine(dir, string.Format("{0}_backup_{1:yyyyMMdd_HHmmss}{2}", fileName, DateTime.Now, ext));

                File.Copy(filePath, bakPath, true);
                return bakPath;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Attempts to restore a corrupted save file from the newest available backup in the same directory.
        /// </summary>
        public static bool RestoreFromNewestBackup(string filePath, out string restoredFrom)
        {
            restoredFrom = "";
            try
            {
                string dir = Path.GetDirectoryName(filePath);
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                string ext = Path.GetExtension(filePath);

                string[] backups = Directory.GetFiles(dir, fileName + "_backup_*" + ext);
                if (backups.Length == 0) return false;

                Array.Sort(backups);
                string newestBackup = backups[backups.Length - 1];

                File.Copy(newestBackup, filePath, true);
                restoredFrom = newestBackup;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
