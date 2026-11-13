using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TitanGameHub
{
    /// <summary>
    /// DirectX & In-Game Lossless Screenshot & Gallery Service:
    /// Captures pristine 32-bit lossless PNG screenshots (Steam F12 equivalent)
    /// directly from the active game window or desktop display and organizes them into game galleries.
    /// </summary>
    public static class DirectXScreenshotGalleryService
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public static bool CaptureGameScreenshot(string gameName, out string savedPath, out string message)
        {
            savedPath = "";
            try
            {
                string safeName = string.IsNullOrEmpty(gameName) ? "TitanGame" : gameName.Replace(":", "").Replace("/", "").Trim();
                string targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Screenshots", safeName);
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                IntPtr fgWindow = GetForegroundWindow();
                RECT rect = new RECT();
                int width = Screen.PrimaryScreen.Bounds.Width;
                int height = Screen.PrimaryScreen.Bounds.Height;
                int left = 0;
                int top = 0;

                if (fgWindow != IntPtr.Zero && GetWindowRect(fgWindow, out rect))
                {
                    int w = rect.Right - rect.Left;
                    int h = rect.Bottom - rect.Top;
                    if (w > 200 && h > 200)
                    {
                        width = w;
                        height = h;
                        left = rect.Left;
                        top = rect.Top;
                    }
                }

                using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
                    }

                    string fileName = string.Format("screenshot_{0:yyyyMMdd_HHmmss}.png", DateTime.Now);
                    savedPath = Path.Combine(targetDir, fileName);
                    bmp.Save(savedPath, ImageFormat.Png);
                }

                message = string.Format("📸 Ekran Görüntüsü Kaydedildi!\n\nKonum: {0}", savedPath);
                return true;
            }
            catch (Exception ex)
            {
                message = "Ekran görüntüsü hatası: " + ex.Message;
                return false;
            }
        }

        public static void OpenGalleryFolder(string gameName)
        {
            try
            {
                string safeName = string.IsNullOrEmpty(gameName) ? "TitanGame" : gameName.Replace(":", "").Replace("/", "").Trim();
                string targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Screenshots", safeName);
                if (!Directory.Exists(targetDir))
                {
                    targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Screenshots");
                    if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                }
                System.Diagnostics.Process.Start("explorer.exe", targetDir);
            }
            catch { }
        }
    }
}
