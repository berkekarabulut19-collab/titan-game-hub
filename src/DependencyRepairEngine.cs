using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TitanGameHub
{
    public class PeBinaryReport
    {
        public bool IsValidPe { get; set; }
        public bool Is64Bit { get; set; }
        public bool IsLargeAddressAware { get; set; }
        public bool IsDotNetAssembly { get; set; }
        public string ArchitectureLabel { get; set; }
        public List<string> ImportedDlls { get; set; }
        public List<string> MissingDlls { get; set; }
        public List<string> RepairedDlls { get; set; }
        public string SummaryText { get; set; }

        public PeBinaryReport()
        {
            ImportedDlls = new List<string>();
            MissingDlls = new List<string>();
            RepairedDlls = new List<string>();
            ArchitectureLabel = "Bilinmiyor";
            SummaryText = "";
        }
    }

    /// <summary>
    /// Phase 1 (v9.0): Native PE Header & Import Address Table (IAT) Analyzer,
    /// 4GB LargeAddressAware (LAA) Patcher, and Missing Runtime/DLL Auto-Repair Engine.
    /// Prevents 0xc000007b and missing DLL startup crashes.
    /// </summary>
    public static class DependencyRepairEngine
    {
        private static readonly string[] _commonRuntimeDirs = new string[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Runtimes")
        };

        /// <summary>
        /// Parses the PE header and Import Directory of a game executable,
        /// locates any missing DLLs, and repairs them from local runtime caches.
        /// </summary>
        public static PeBinaryReport InspectAndRepairGameBinary(GameProfile profile)
        {
            PeBinaryReport report = new PeBinaryReport();
            if (profile == null || string.IsNullOrEmpty(profile.ExecutablePath) || !File.Exists(profile.ExecutablePath))
            {
                report.SummaryText = "Oyun .EXE dosyası bulunamadı. Önce oyunu kurun veya .EXE seçin.";
                return report;
            }

            try
            {
                byte[] data;
                using (FileStream fs = new FileStream(profile.ExecutablePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int toRead = (int)Math.Min(4L * 1024L * 1024L, fs.Length);
                    data = new byte[toRead];
                    fs.Read(data, 0, toRead);
                }
                if (data.Length < 0x200 || data[0] != 0x4D || data[1] != 0x5A) // "MZ"
                {
                    report.SummaryText = "Geçersiz PE başlığı (MZ imzası yok).";
                    return report;
                }

                int peOffset = BitConverter.ToInt32(data, 0x3C);
                if (peOffset <= 0 || peOffset + 0x108 >= data.Length)
                {
                    report.SummaryText = "PE başlık ofseti dosya sınırları dışında.";
                    return report;
                }

                if (data[peOffset] != 0x50 || data[peOffset + 1] != 0x45) // "PE\0\0"
                {
                    report.SummaryText = "PE\\0\\0 imzası doğrulanamadı.";
                    return report;
                }

                report.IsValidPe = true;
                ushort machine = BitConverter.ToUInt16(data, peOffset + 4);
                ushort numberOfSections = BitConverter.ToUInt16(data, peOffset + 6);
                ushort sizeOfOptionalHeader = BitConverter.ToUInt16(data, peOffset + 20);
                ushort characteristics = BitConverter.ToUInt16(data, peOffset + 22);

                report.Is64Bit = (machine == 0x8664);
                report.IsLargeAddressAware = (characteristics & 0x0020) != 0; // IMAGE_FILE_LARGE_ADDRESS_AWARE
                report.ArchitectureLabel = report.Is64Bit ? "x64 (64-Bit Native)" : (report.IsLargeAddressAware ? "x86 (4GB LAA Aktif)" : "x86 (2GB Sınırlı)");

                int optHeaderOffset = peOffset + 24;
                ushort magic = BitConverter.ToUInt16(data, optHeaderOffset);
                bool isPe32Plus = (magic == 0x20B);

                // Check CLR Runtime Header (DataDirectory[14])
                int dataDirOffset = optHeaderOffset + (isPe32Plus ? 112 : 96);
                int clrRva = BitConverter.ToInt32(data, dataDirOffset + (14 * 8));
                report.IsDotNetAssembly = (clrRva > 0);

                // Import Directory Table (DataDirectory[1])
                int importRva = BitConverter.ToInt32(data, dataDirOffset + (1 * 8));
                int sectionTableOffset = optHeaderOffset + sizeOfOptionalHeader;

                if (importRva > 0)
                {
                    int importFileOffset = RvaToFileOffset(data, sectionTableOffset, numberOfSections, importRva);
                    if (importFileOffset > 0 && importFileOffset + 20 < data.Length)
                    {
                        for (int descIdx = 0; descIdx < 256; descIdx++)
                        {
                            int curDesc = importFileOffset + (descIdx * 20);
                            if (curDesc + 20 > data.Length) break;

                            int nameRva = BitConverter.ToInt32(data, curDesc + 12);
                            int firstThunk = BitConverter.ToInt32(data, curDesc + 16);
                            if (nameRva == 0 && firstThunk == 0) break;

                            int nameFileOffset = RvaToFileOffset(data, sectionTableOffset, numberOfSections, nameRva);
                            if (nameFileOffset > 0 && nameFileOffset < data.Length)
                            {
                                string dllName = ReadAsciiZ(data, nameFileOffset);
                                if (!string.IsNullOrEmpty(dllName) && !report.ImportedDlls.Contains(dllName))
                                {
                                    report.ImportedDlls.Add(dllName);
                                }
                            }
                        }
                    }
                }

                // Scan for missing DLLs in game directory and system directories
                string gameDir = Path.GetDirectoryName(profile.ExecutablePath);
                foreach (string dll in report.ImportedDlls)
                {
                    if (IsSystemCoreDll(dll)) continue;

                    string inGamePath = Path.Combine(gameDir, dll);
                    if (File.Exists(inGamePath)) continue;

                    bool existsInSystem = false;
                    string systemSourcePath = null;

                    if (report.Is64Bit)
                    {
                        string sys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), dll);
                        if (File.Exists(sys32))
                        {
                            existsInSystem = true;
                            systemSourcePath = sys32;
                        }
                    }
                    else
                    {
                        string sysWow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), dll);
                        if (File.Exists(sysWow))
                        {
                            existsInSystem = true;
                            systemSourcePath = sysWow;
                        }
                    }

                    // Check local Runtimes folder
                    string localRuntime = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Runtimes", dll);
                    if (!existsInSystem && File.Exists(localRuntime))
                    {
                        try
                        {
                            File.Copy(localRuntime, inGamePath, true);
                            report.RepairedDlls.Add(dll);
                            existsInSystem = true;
                        }
                        catch { }
                    }
                    else if (existsInSystem && IsRedistDll(dll) && !File.Exists(inGamePath) && !string.IsNullOrEmpty(systemSourcePath))
                    {
                        // Side-by-side copy for critical redistributables
                        try
                        {
                            File.Copy(systemSourcePath, inGamePath, false);
                            report.RepairedDlls.Add(dll + " (Side-by-Side)");
                        }
                        catch { }
                    }

                    if (!existsInSystem)
                    {
                        // If it's steam_api64.dll or steam_api.dll, deploy our Spacewar P2P configuration
                        if (dll.IndexOf("steam_api", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            profile.DeployOnlineFix("480");
                            report.RepairedDlls.Add(dll + " (Online-Fix 480)");
                        }
                        else
                        {
                            report.MissingDlls.Add(dll);
                        }
                    }
                }

                // If 32-bit and not LargeAddressAware, automatically apply 4GB LAA Patch!
                if (!report.Is64Bit && !report.IsLargeAddressAware)
                {
                    if (EnableLargeAddressAwarePatch(profile.ExecutablePath))
                    {
                        report.IsLargeAddressAware = true;
                        report.ArchitectureLabel = "x86 (4GB LAA Yamalandı)";
                        report.RepairedDlls.Add("IMAGE_FILE_LARGE_ADDRESS_AWARE (4GB RAM Kilidi Açıldı)");
                    }
                }

                report.SummaryText = string.Format(
                    "[PE ANALİZİ] Mimari: {0} | Bağımlı DLL: {1} adet | Onarılan: {2} | Eksik: {3}",
                    report.ArchitectureLabel,
                    report.ImportedDlls.Count,
                    report.RepairedDlls.Count,
                    report.MissingDlls.Count);
            }
            catch (Exception ex)
            {
                report.SummaryText = "PE/IAT analiz hatası: " + ex.Message;
            }

            return report;
        }

        /// <summary>
        /// Patches a 32-bit PE binary to enable IMAGE_FILE_LARGE_ADDRESS_AWARE (0x0020)
        /// so 32-bit games can address 4GB of virtual memory instead of crashing at 2GB.
        /// </summary>
        public static bool EnableLargeAddressAwarePatch(string exePath)
        {
            try
            {
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return false;

                string backupPath = exePath + ".laa_bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(exePath, backupPath, false);
                }

                byte[] bytes = File.ReadAllBytes(exePath);
                int peOffset = BitConverter.ToInt32(bytes, 0x3C);
                if (peOffset <= 0 || peOffset + 24 >= bytes.Length) return false;

                ushort characteristics = BitConverter.ToUInt16(bytes, peOffset + 22);
                characteristics |= 0x0020; // IMAGE_FILE_LARGE_ADDRESS_AWARE
                byte[] charBytes = BitConverter.GetBytes(characteristics);
                bytes[peOffset + 22] = charBytes[0];
                bytes[peOffset + 23] = charBytes[1];

                File.WriteAllBytes(exePath, bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int RvaToFileOffset(byte[] data, int sectionTableOffset, ushort numSections, int rva)
        {
            for (int i = 0; i < numSections; i++)
            {
                int secOffset = sectionTableOffset + (i * 40);
                if (secOffset + 40 > data.Length) break;

                int virtualSize = BitConverter.ToInt32(data, secOffset + 8);
                int virtualAddress = BitConverter.ToInt32(data, secOffset + 12);
                int sizeOfRawData = BitConverter.ToInt32(data, secOffset + 16);
                int pointerToRawData = BitConverter.ToInt32(data, secOffset + 20);

                int maxSecSize = Math.Max(virtualSize, sizeOfRawData);
                if (rva >= virtualAddress && rva < virtualAddress + maxSecSize)
                {
                    return pointerToRawData + (rva - virtualAddress);
                }
            }
            return -1;
        }

        private static string ReadAsciiZ(byte[] data, int offset)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = offset; i < data.Length && i < offset + 128; i++)
            {
                if (data[i] == 0) break;
                sb.Append((char)data[i]);
            }
            return sb.ToString();
        }

        private static bool IsSystemCoreDll(string dll)
        {
            string lower = dll.ToLowerInvariant();
            return lower == "kernel32.dll" || lower == "user32.dll" || lower == "gdi32.dll" ||
                   lower == "ntdll.dll" || lower == "advapi32.dll" || lower == "shell32.dll" ||
                   lower == "ole32.dll" || lower == "oleaut32.dll" || lower == "ws2_32.dll" ||
                   lower == "winmm.dll" || lower == "imm32.dll" || lower == "setupapi.dll" ||
                   lower == "version.dll" || lower == "shlwapi.dll" || lower == "crypt32.dll" ||
                   lower == "bcrypt.dll" || lower == "mscoree.dll" || lower.StartsWith("api-ms-win-");
        }

        private static bool IsRedistDll(string dll)
        {
            string lower = dll.ToLowerInvariant();
            return lower.StartsWith("vcruntime") || lower.StartsWith("msvcp") ||
                   lower.StartsWith("d3dx9_") || lower.StartsWith("d3dcompiler_") ||
                   lower.StartsWith("xinput") || lower.StartsWith("x3daudio") ||
                   lower.StartsWith("xapofx") || lower == "openal32.dll";
        }
    }
}
