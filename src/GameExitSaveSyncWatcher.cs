using System;
using System.Diagnostics;
using System.Threading;

namespace TitanGameHub
{
    /// <summary>
    /// Auto-Save & Cloud Sync on Game Exit Watcher:
    /// Automatically monitors running game processes.
    /// The exact microsecond game.exe exits, automatically takes a snapshot of the newest save,
    /// updates the Co-Op Cloud Vault, and syncs progress across connected devices without manual clicks.
    /// </summary>
    public static class GameExitSaveSyncWatcher
    {
        public static event Action<GameProfile, string> OnSaveAutoSynced;

        public static void WatchGameProcess(GameProfile gp, Process proc, Action onFinishedCallback)
        {
            if (gp == null || proc == null) return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    DateTime launchTime = DateTime.Now;

                    // Wait for process exit
                    proc.WaitForExit();

                    TimeSpan sessionDuration = DateTime.Now - launchTime;
                    int addedMinutes = (int)Math.Max(1, sessionDuration.TotalMinutes);
                    gp.PlayTimeMinutes += addedMinutes;
                    gp.LastPlayedText = DateTime.Now.ToString("dd.MM.yyyy HH:mm");

                    // 1. Instant Save Backup to Co-Op Vault
                    string backupMsg;
                    SaveSyncService.CreateSaveBackup(gp, out backupMsg);

                    // 2. Auto-resolve & promote latest progress in vault
                    string conflictMsg;
                    SaveConflictResolverService.AutoResolveAndMerge(gp, true, out conflictMsg);

                    // 3. Auto-apply any staged update that arrived while playing
                    string delayedStatus;
                    if (SmartUpdateShieldService.TryApplyPendingDelayedUpdate(gp, null, out delayedStatus))
                    {
                        backupMsg += "\n🚀 Oyun oynanırken arka planda hazırlanan güncelleme oyundan çıkışınızla birlikte otomatik uygulandı!";
                    }

                    // 4. Auto-export newly compiled shaders to Shader Pre-Cache Pool (Zero-Stutter Pool)
                    try
                    {
                        string outShaderPath, shaderStatus;
                        ShaderPreCacheService.ExportGameShaderCache(gp, out outShaderPath, out shaderStatus);
                    }
                    catch { }

                    // 5. Update Discord Rich Presence back to Idle Hub state
                    try
                    {
                        DiscordRichPresenceService.SetIdleInHub();
                    }
                    catch { }

                    string summary = string.Format(
                        "💾 Otomatik Save Eşitlemesi Tamamlandı (+{0} dk oynandı): {1}",
                        addedMinutes, backupMsg);

                    if (OnSaveAutoSynced != null)
                    {
                        OnSaveAutoSynced(gp, summary);
                    }

                    if (onFinishedCallback != null)
                    {
                        onFinishedCallback();
                    }

                    // 6. Check if Titan Game Hub application update was waiting for game exit!
                    try
                    {
                        if (AutoUpdateService.HasPendingAppHotSwap)
                        {
                            Thread.Sleep(1500);
                            AutoUpdateService.ExecutePendingAppHotSwapIfReady();
                        }
                    }
                    catch { }
                }
                catch { }
            });
        }
    }
}
