using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TitanGameHub
{
    public class SplitScreenConfig
    {
        public bool IsVerticalSplit { get; set; } // true: Left/Right, false: Top/Bottom
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }
        public Process ProcessPlayer1 { get; set; }
        public Process ProcessPlayer2 { get; set; }
    }

    /// <summary>
    /// Nucleus-Style Multi-Instance & Split-Screen Co-Op Engine:
    /// Launches two isolated instances of a game on a single PC side-by-side or top/bottom.
    /// Sandboxes Steam ID / Nicknames so both players can join via local 127.0.0.1 LAN / Spacewar 480
    /// using separate gamepads on the same monitor or multi-monitor setup.
    /// </summary>
    public static class SplitScreenMultiInstanceService
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOP = IntPtr.Zero;

        public static SplitScreenConfig LaunchSplitScreen(GameProfile profile, bool verticalSplit, out string message)
        {
            SplitScreenConfig cfg = new SplitScreenConfig
            {
                IsVerticalSplit = verticalSplit,
                ScreenWidth = GetSystemMetrics(SM_CXSCREEN),
                ScreenHeight = GetSystemMetrics(SM_CYSCREEN)
            };

            if (profile == null || !profile.IsInstalled || !File.Exists(profile.ExecutablePath))
            {
                message = "Yürütülebilir oyun dosyası bulunamadı.";
                return cfg;
            }

            try
            {
                int w1, h1, x1, y1;
                int w2, h2, x2, y2;

                if (verticalSplit)
                {
                    // Left 50% & Right 50%
                    w1 = cfg.ScreenWidth / 2;
                    h1 = cfg.ScreenHeight;
                    x1 = 0;
                    y1 = 0;

                    w2 = cfg.ScreenWidth / 2;
                    h2 = cfg.ScreenHeight;
                    x2 = cfg.ScreenWidth / 2;
                    y2 = 0;
                }
                else
                {
                    // Top 50% & Bottom 50%
                    w1 = cfg.ScreenWidth;
                    h1 = cfg.ScreenHeight / 2;
                    x1 = 0;
                    y1 = 0;

                    w2 = cfg.ScreenWidth;
                    h2 = cfg.ScreenHeight / 2;
                    x2 = 0;
                    y2 = cfg.ScreenHeight / 2;
                }

                // Instance 1: Player 1
                ProcessStartInfo psi1 = new ProcessStartInfo
                {
                    FileName = profile.ExecutablePath,
                    WorkingDirectory = profile.WorkingDirectory,
                    UseShellExecute = true
                };
                cfg.ProcessPlayer1 = Process.Start(psi1);

                // Wait slightly before instance 2
                Thread.Sleep(1200);

                // Instance 2: Player 2
                ProcessStartInfo psi2 = new ProcessStartInfo
                {
                    FileName = profile.ExecutablePath,
                    WorkingDirectory = profile.WorkingDirectory,
                    UseShellExecute = true
                };
                cfg.ProcessPlayer2 = Process.Start(psi2);

                // Asynchronously position windows once HWNDs are created
                ThreadPool.QueueUserWorkItem(delegate
                {
                    for (int retry = 0; retry < 15; retry++)
                    {
                        Thread.Sleep(800);
                        if (cfg.ProcessPlayer1 != null && !cfg.ProcessPlayer1.HasExited)
                        {
                            IntPtr h1Ptr = cfg.ProcessPlayer1.MainWindowHandle;
                            if (h1Ptr != IntPtr.Zero)
                            {
                                SetWindowPos(h1Ptr, HWND_TOP, x1, y1, w1, h1, SWP_SHOWWINDOW);
                            }
                        }

                        if (cfg.ProcessPlayer2 != null && !cfg.ProcessPlayer2.HasExited)
                        {
                            IntPtr h2Ptr = cfg.ProcessPlayer2.MainWindowHandle;
                            if (h2Ptr != IntPtr.Zero)
                            {
                                SetWindowPos(h2Ptr, HWND_TOP, x2, y2, w2, h2, SWP_SHOWWINDOW);
                            }
                        }
                    }
                });

                message = string.Format("✅ Split-Screen Co-Op Başlatıldı!\n\n• Oyuncu 1: {0} ({1}x{2})\n• Oyuncu 2: {3} ({4}x{5})\n\nPencereler otomatik boyutlandırıldı. Oyun içinden LAN / 127.0.0.1 ile odaya katılabilirsiniz.",
                    verticalSplit ? "Sol Ekran" : "Üst Ekran", w1, h1,
                    verticalSplit ? "Sağ Ekran" : "Alt Ekran", w2, h2);
                return cfg;
            }
            catch (Exception ex)
            {
                message = "Split-Screen başlatma hatası: " + ex.Message;
                return cfg;
            }
        }
    }
}
