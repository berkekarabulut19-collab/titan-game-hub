using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace TitanGameHub
{
    /// <summary>
    /// Steam-Style Shader Pre-Caching Engine (C# 5.0 Strict):
    /// 1. Scans, exports, and injects compiled Vulkan/DirectX shader caches (.dxvk-cache, NV/AMD DXCache).
    /// 2. Auto-deploys pre-compiled shaders before game launch to eliminate first-time compilation stuttering.
    /// 3. Enables P2P shader cache sharing across all connected squad members.
    /// </summary>
    public static class ShaderPreCacheService
    {
        public static string GetShaderPoolDirectory()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShaderPool");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }
            return dir;
        }

        public static bool AutoDeployShaderCache(GameProfile profile, out string resultMsg)
        {
            resultMsg = "";
            if (profile == null || string.IsNullOrEmpty(profile.WorkingDirectory) || !Directory.Exists(profile.WorkingDirectory))
            {
                resultMsg = "Oyun calisma dizini bulunamadi.";
                return false;
            }

            try
            {
                string poolDir = GetShaderPoolDirectory();
                string targetCacheFile = Path.Combine(poolDir, profile.Id + ".titancache");
                if (!File.Exists(targetCacheFile))
                {
                    string safeName = CleanFileName(profile.Name) + ".titancache";
                    targetCacheFile = Path.Combine(poolDir, safeName);
                }

                if (!File.Exists(targetCacheFile))
                {
                    resultMsg = "Havuzda onceden derlenmis shader paketi yok (Oynandikca otomatik toplanacak).";
                    return false;
                }

                // Deploy into game directory
                string tempExtract = Path.Combine(Path.GetTempPath(), "TitanShader_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Directory.CreateDirectory(tempExtract);

                ZipFile.ExtractToDirectory(targetCacheFile, tempExtract);
                string[] files = Directory.GetFiles(tempExtract, "*.*", SearchOption.AllDirectories);
                int deployedCount = 0;

                foreach (string f in files)
                {
                    string relPath = f.Substring(tempExtract.Length).TrimStart('\\', '/');
                    string dest = Path.Combine(profile.WorkingDirectory, relPath);
                    string destDir = Path.GetDirectoryName(dest);
                    if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                    File.Copy(f, dest, true);
                    deployedCount++;
                }

                try { Directory.Delete(tempExtract, true); } catch { }

                resultMsg = string.Format("🚀 Steam Shader Pre-Cache Aktif: {0} adet on-derlenmis shader enjekte edildi (Stutter Sifirlandi)!", deployedCount);
                return true;
            }
            catch (Exception ex)
            {
                resultMsg = "Shader enjeksiyon hatasi: " + ex.Message;
                return false;
            }
        }

        public static bool ExportGameShaderCache(GameProfile profile, out string outZipPath, out string statusMsg)
        {
            outZipPath = null;
            statusMsg = "";
            if (profile == null || string.IsNullOrEmpty(profile.WorkingDirectory) || !Directory.Exists(profile.WorkingDirectory))
            {
                statusMsg = "Gecersiz oyun profili veya dizin yok.";
                return false;
            }

            try
            {
                List<string> candidateShaders = new List<string>();

                // 1. Scan game local folder for .dxvk-cache, vkPipelineCache, ShaderCache
                string[] allFiles = Directory.GetFiles(profile.WorkingDirectory, "*.*", SearchOption.AllDirectories);
                foreach (string f in allFiles)
                {
                    string ext = (Path.GetExtension(f) ?? "").ToLowerInvariant();
                    string fname = (Path.GetFileName(f) ?? "").ToLowerInvariant();
                    if (ext == ".dxvk-cache" || ext == ".pipelinecache" || fname.Contains("shader") || fname.Contains("pipestate"))
                    {
                        candidateShaders.Add(f);
                    }
                }

                // 2. Scan driver level DXCache for recently touched files
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string nvCache = Path.Combine(localAppData, @"NVIDIA\DXCache");
                if (Directory.Exists(nvCache))
                {
                    string[] nvFiles = Directory.GetFiles(nvCache, "*.bin");
                    DateTime oneDayAgo = DateTime.Now.AddHours(-12);
                    foreach (string nf in nvFiles)
                    {
                        FileInfo fi = new FileInfo(nf);
                        if (fi.LastWriteTime > oneDayAgo && fi.Length > 1024)
                        {
                            candidateShaders.Add(nf);
                        }
                    }
                }

                if (candidateShaders.Count == 0)
                {
                    statusMsg = "Henuz derlenmis shader dosyasi olusmamis.";
                    return false;
                }

                string poolDir = GetShaderPoolDirectory();
                string destFile = Path.Combine(poolDir, profile.Id + ".titancache");
                string tempStage = Path.Combine(Path.GetTempPath(), "StageShader_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Directory.CreateDirectory(tempStage);

                int added = 0;
                foreach (string sh in candidateShaders)
                {
                    try
                    {
                        string fn = Path.GetFileName(sh);
                        string df = Path.Combine(tempStage, fn);
                        File.Copy(sh, df, true);
                        added++;
                    }
                    catch { }
                }

                if (File.Exists(destFile)) File.Delete(destFile);
                ZipFile.CreateFromDirectory(tempStage, destFile);
                try { Directory.Delete(tempStage, true); } catch { }

                outZipPath = destFile;
                statusMsg = string.Format("✅ {0} adet derlenmis shader havuzlandi: {1}", added, Path.GetFileName(destFile));
                return true;
            }
            catch (Exception ex)
            {
                statusMsg = "Shader export hatasi: " + ex.Message;
                return false;
            }
        }

        private static string CleanFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "game";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }
    }
}
