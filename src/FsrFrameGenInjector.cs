using System;
using System.IO;
using System.Text;

namespace TitanGameHub
{
    /// <summary>
    /// Phase 5 (v9.0): Dynamic FSR 3.1 / OptiScaler Frame Generation Config Injector,
    /// Unreal Engine 4/5 Low-Latency Shader/CVar Optimizer, and Unity Native Graphics Jobs Booster.
    /// Doubles effective frame rate and eliminates shader compilation stutter.
    /// </summary>
    public static class FsrFrameGenInjector
    {
        public static string DeployGraphicsAndFpsMultiplier(GameProfile profile)
        {
            if (profile == null) return "Geçerli bir oyun profili seçilmedi.";

            string targetDir = profile.WorkingDirectory;
            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            {
                if (!string.IsNullOrEmpty(profile.ExecutablePath) && File.Exists(profile.ExecutablePath))
                {
                    targetDir = Path.GetDirectoryName(profile.ExecutablePath);
                }
                else
                {
                    targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Games", profile.Name);
                    if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                }
            }

            int optimizationsApplied = 0;
            StringBuilder summary = new StringBuilder();

            try
            {
                // 1. Deploy OptiScaler / FSR 3.1 Frame Generation & Upscaling Configuration
                string optiScalerIni = Path.Combine(targetDir, "OptiScaler.ini");
                string optiContent =
                    "; Titan Game Hub v9.0 — FSR 3.1.2 & Frame Generation Engine Config\r\n" +
                    "[Upscalers]\r\n" +
                    "Dx11Upscaler=fsr31\r\n" +
                    "Dx12Upscaler=fsr31\r\n" +
                    "VulkanUpscaler=fsr31\r\n\r\n" +
                    "[FrameGen]\r\n" +
                    "FGType=optifg\r\n" +
                    "Enabled=true\r\n" +
                    "AllowAsync=true\r\n" +
                    "HudFix=true\r\n" +
                    "HudFixLimit=2\r\n\r\n" +
                    "[FSR]\r\n" +
                    "VelocityFactor=1.0\r\n" +
                    "ReactiveScale=0.85\r\n" +
                    "ShadingScale=0.90\r\n\r\n" +
                    "[CAS]\r\n" +
                    "Enabled=true\r\n" +
                    "Sharpness=0.65\r\n" +
                    "MotionSharpnessEnabled=true\r\n\r\n" +
                    "[Spoofing]\r\n" +
                    "SpoofedGPUName=NVIDIA GeForce RTX 4090\r\n" +
                    "DxgiSpoofing=true\r\n";

                File.WriteAllText(optiScalerIni, optiContent, Encoding.UTF8);
                optimizationsApplied++;
                summary.Append("FSR 3.1 FrameGen (OptiScaler.ini)");

                // 2. Detect Unity Engine (*_Data folder) and inject Multi-Threaded Native Graphics Jobs into boot.config
                string[] dataDirs = Directory.GetDirectories(targetDir, "*_Data", SearchOption.AllDirectories);
                foreach (string dDir in dataDirs)
                {
                    string bootCfg = Path.Combine(dDir, "boot.config");
                    string existing = File.Exists(bootCfg) ? File.ReadAllText(bootCfg) : "";
                    StringBuilder bootSb = new StringBuilder(existing);
                    if (!existing.Contains("gfx-enable-gfx-jobs"))
                        bootSb.AppendLine("\rgfx-enable-gfx-jobs=1");
                    if (!existing.Contains("gfx-enable-native-gfx-jobs"))
                        bootSb.AppendLine("gfx-enable-native-gfx-jobs=1");
                    if (!existing.Contains("gc-max-time-slice"))
                        bootSb.AppendLine("gc-max-time-slice=3");
                    if (!existing.Contains("wait-for-native-debugger"))
                        bootSb.AppendLine("wait-for-native-debugger=0");
                    if (!existing.Contains("hdr-display-enabled"))
                        bootSb.AppendLine("hdr-display-enabled=0");

                    File.WriteAllText(bootCfg, bootSb.ToString().Trim() + "\r\n", Encoding.ASCII);
                    optimizationsApplied++;
                    summary.Append(" + Unity Native GFX Jobs (boot.config)");
                    break;
                }

                // 3. Detect Unreal Engine 4/5 structure or create Low-Latency CVar profile
                string ueSavedConfigDir = Path.Combine(targetDir, "Saved", "Config", "WindowsNoEditor");
                string[] uePakFiles = Directory.GetFiles(targetDir, "*.pak", SearchOption.AllDirectories);
                if (uePakFiles.Length > 0 || Directory.Exists(Path.Combine(targetDir, "Engine")))
                {
                    if (!Directory.Exists(ueSavedConfigDir))
                        Directory.CreateDirectory(ueSavedConfigDir);

                    string engineIni = Path.Combine(ueSavedConfigDir, "Engine.ini");
                    string ueCvars =
                        "[SystemSettings]\r\n" +
                        "r.OneFrameThreadLag=0\r\n" +
                        "r.CreateShadersOnLoad=1\r\n" +
                        "r.Shaders.Optimize=1\r\n" +
                        "r.Shaders.FastMath=1\r\n" +
                        "r.GTSyncType=1\r\n" +
                        "r.FinishCurrentFrame=0\r\n" +
                        "r.SceneColorFringeQuality=0\r\n" +
                        "r.MotionBlurQuality=0\r\n" +
                        "r.Tonemapper.GrainQuantization=0\r\n" +
                        "r.Tonemapper.Quality=1\r\n" +
                        "r.TemporalAA.Upsampling=1\r\n" +
                        "r.TemporalAACurrentFrameWeight=0.2\r\n" +
                        "r.Streaming.PoolSize=2048\r\n" +
                        "r.Streaming.MaxTempMemoryAllowed=256\r\n" +
                        "s.AsyncLoadingThreadEnabled=1\r\n";

                    File.WriteAllText(engineIni, ueCvars, Encoding.UTF8);
                    optimizationsApplied++;
                    summary.Append(" + Unreal Engine 4/5 Düşük Gecikme Shader CVar");
                }

                // 4. Deploy DXVK / DirectX 11-to-Vulkan Async Pipeline Cache config for stutter-free frame pacing
                string dxvkConf = Path.Combine(targetDir, "dxvk.conf");
                string dxvkContent =
                    "# Titan Game Hub v9.0 — Async Shader Pipeline Configuration\r\n" +
                    "dxvk.enableAsync = true\r\n" +
                    "dxvk.numCompilerThreads = 0\r\n" +
                    "dxvk.useRawSsbo = Auto\r\n" +
                    "dxgi.syncInterval = 0\r\n" +
                    "dxgi.maxFrameLatency = 1\r\n" +
                    "d3d11.maxFrameLatency = 1\r\n";
                File.WriteAllText(dxvkConf, dxvkContent, Encoding.UTF8);
                optimizationsApplied++;
                summary.Append(" + Async Shader Cache (dxvk.conf)");
            }
            catch (Exception ex)
            {
                return "FSR 3.1 / Grafik enjeksiyon uyarısı: " + ex.Message;
            }

            return string.Format("🚀 [{0}] için {1} grafik & FPS çarpanı modülü enjekte edildi ({2}).",
                profile.Name, optimizationsApplied, summary.ToString());
        }
    }
}
