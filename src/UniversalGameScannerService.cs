using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TitanGameHub
{
    public class DiscoveredGameEntry
    {
        public string Title { get; set; }
        public string ExecutablePath { get; set; }
        public string RootDirectory { get; set; }
        public string SourcePlatform { get; set; } // Steam, Epic, GOG, Custom
        public string DetectedAppId { get; set; }
        public long FileSizeBytes { get; set; }
    }

    /// <summary>
    /// Universal Game Library Scanner & Auto-Importer:
    /// Automatically detects games installed across Steam, Epic Games, GOG Galaxy,
    /// and local drives (C:\Games, D:\Games, etc.).
    /// Imports them into Titan Game Hub with 1-click, instantly applying Spacewar 480 and CPU optimizations.
    /// </summary>
    public static class UniversalGameScannerService
    {
        public static List<DiscoveredGameEntry> ScanInstalledGames()
        {
            List<DiscoveredGameEntry> results = new List<DiscoveredGameEntry>();
            List<string> candidateFolders = new List<string>();

            // Kök listesi artık GamePathResolver'dan geliyor: tüm sabit diskler,
            // Steam kütüphaneleri (libraryfolders.vdf), Epic/GOG ve portatif Games klasörü.
            // Sürücü harfi sabit kodlu değil -> başka bilgisayarda da tüm oyunlar bulunur.
            List<string> resolverRoots = GamePathResolver.GetLibraryRoots(true);
            for (int r = 0; r < resolverRoots.Count; r++)
            {
                if (!candidateFolders.Contains(resolverRoots[r]))
                    candidateFolders.Add(resolverRoots[r]);
            }

            // Ek olarak: Epic / GOG varsayılan kurulum dizinleri
            string epicPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games");
            if (Directory.Exists(epicPath) && !candidateFolders.Contains(epicPath)) candidateFolders.Add(epicPath);

            string gogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy", "Games");
            if (Directory.Exists(gogPath) && !candidateFolders.Contains(gogPath)) candidateFolders.Add(gogPath);

            // Scan each root directory
            for (int i = 0; i < candidateFolders.Count; i++)
            {
                string cFolder = candidateFolders[i];
                try
                {
                    string[] gameDirs = Directory.GetDirectories(cFolder);
                    for (int g = 0; g < gameDirs.Length; g++)
                    {
                        try
                        {
                            string gDir = gameDirs[g];
                            string dirName = Path.GetFileName(gDir) ?? "";

                            // Skip common non-game or redist directories
                            if (dirName.Equals("CommonRedist", StringComparison.OrdinalIgnoreCase) ||
                                dirName.Equals("Steamworks Shared", StringComparison.OrdinalIgnoreCase) ||
                                dirName.StartsWith("."))
                                continue;

                            // Find primary .exe safely (klasör adı–exe skorlaması + installer/redist filtresi)
                            string bestExe = null;
                            try
                            {
                                bestExe = ProfileStore.FindMainExeInFolder(gDir);
                                if (!GamePathResolver.IsPlausibleMainExe(bestExe)) bestExe = null;
                            }
                            catch { }

                            if (!string.IsNullOrEmpty(bestExe))
                            {
                                string appId = "480";
                                string appIdFile = Path.Combine(gDir, "steam_appid.txt");
                                if (File.Exists(appIdFile))
                                {
                                    try { appId = File.ReadAllText(appIdFile).Trim(); } catch { }
                                }

                                string source = cFolder.Contains("Steam") ? "Steam" : (cFolder.Contains("Epic") ? "Epic Games" : (cFolder.Contains("GOG") ? "GOG" : "Yerel Disk"));

                                results.Add(new DiscoveredGameEntry
                                {
                                    Title = dirName,
                                    ExecutablePath = bestExe,
                                    RootDirectory = gDir,
                                    SourcePlatform = source,
                                    DetectedAppId = appId
                                });
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return results;
        }

        public static int ImportDiscoveredGames(List<DiscoveredGameEntry> discovered, List<GameProfile> existingProfiles, out string report)
        {
            int addedCount = 0;
            if (discovered == null || discovered.Count == 0)
            {
                report = "İçe aktarılacak yeni oyun bulunamadı.";
                return 0;
            }

            for (int i = 0; i < discovered.Count; i++)
            {
                DiscoveredGameEntry d = discovered[i];
                bool alreadyExists = false;

                for (int e = 0; e < existingProfiles.Count; e++)
                {
                    if (string.Equals(existingProfiles[e].ExecutablePath, d.ExecutablePath, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(existingProfiles[e].Name, d.Title, StringComparison.OrdinalIgnoreCase))
                    {
                        alreadyExists = true;
                        existingProfiles[e].ExecutablePath = d.ExecutablePath;
                        existingProfiles[e].WorkingDirectory = d.RootDirectory;
                        break;
                    }
                }

                if (!alreadyExists)
                {
                    GameProfile gp = new GameProfile
                    {
                        Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                        Name = d.Title,
                        ExecutablePath = d.ExecutablePath,
                        WorkingDirectory = d.RootDirectory,
                        SteamAppId = d.DetectedAppId,
                        Category = d.SourcePlatform + " • Otomatik Algılandı",
                        FileSize = "Kurulu",
                        Version = "v1.0"
                    };
                    existingProfiles.Add(gp);
                    addedCount++;
                }
            }

            ProfileStore.SaveProfiles(existingProfiles);
            report = string.Format("✅ {0} Adet Kurulu Oyun Tespit Edildi ve Kütüphaneye İçe Aktarıldı!", addedCount);
            return addedCount;
        }
    }
}
