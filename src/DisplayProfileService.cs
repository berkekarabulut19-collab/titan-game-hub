using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TitanGameHub
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;

        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public short dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    public class DisplayResolutionItem
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int RefreshRate { get; set; }

        public string DisplayText
        {
            get { return string.Format("{0} x {1} @ {2}Hz", Width, Height, RefreshRate); }
        }
    }

    public static class DisplayProfileService
    {
        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern int ChangeDisplaySettings(IntPtr devMode, int flags);

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int CDS_UPDATEREGISTRY = 0x01;
        private const int CDS_TEST = 0x02;
        private const int DISP_CHANGE_SUCCESSFUL = 0;

        public const int DM_PELSWIDTH = 0x00080000;
        public const int DM_PELSHEIGHT = 0x00100000;
        public const int DM_DISPLAYFREQUENCY = 0x00400000;

        private static DEVMODE _savedDesktopMode;
        private static bool _hasSavedMode = false;

        public static void SaveDesktopMode()
        {
            try
            {
                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
                {
                    _savedDesktopMode = dm;
                    _hasSavedMode = true;
                }
            }
            catch { }
        }

        public static bool ApplyGameDisplayMode(int width, int height, int refreshRate, out string statusMsg)
        {
            statusMsg = "";
            if (!_hasSavedMode) SaveDesktopMode();

            try
            {
                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                dm.dmPelsWidth = width;
                dm.dmPelsHeight = height;
                dm.dmDisplayFrequency = refreshRate;
                dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | (refreshRate > 0 ? DM_DISPLAYFREQUENCY : 0);

                int res = ChangeDisplaySettings(ref dm, 0);
                if (res == DISP_CHANGE_SUCCESSFUL)
                {
                    statusMsg = string.Format("Monitör {0}x{1} @ {2}Hz moduna kilitlendi.", width, height, refreshRate);
                    return true;
                }
                else
                {
                    statusMsg = "Çözünürlük değiştirilemedi. Hata kodu: " + res;
                    return false;
                }
            }
            catch (Exception ex)
            {
                statusMsg = "Monitör ayar hatası: " + ex.Message;
                return false;
            }
        }

        public static void RestoreDesktopMode()
        {
            try
            {
                if (_hasSavedMode)
                {
                    ChangeDisplaySettings(ref _savedDesktopMode, 0);
                }
                else
                {
                    ChangeDisplaySettings(IntPtr.Zero, 0);
                }
            }
            catch { }
        }

        public static List<DisplayResolutionItem> GetSupportedResolutions()
        {
            List<DisplayResolutionItem> list = new List<DisplayResolutionItem>();
            HashSet<string> seen = new HashSet<string>();

            try
            {
                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                int i = 0;
                while (EnumDisplaySettings(null, i, ref dm))
                {
                    if (dm.dmBitsPerPel >= 32 && dm.dmPelsWidth >= 1280)
                    {
                        string key = string.Format("{0}x{1}@{2}", dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
                        if (!seen.Contains(key))
                        {
                            seen.Add(key);
                            list.Add(new DisplayResolutionItem
                            {
                                Width = dm.dmPelsWidth,
                                Height = dm.dmPelsHeight,
                                RefreshRate = dm.dmDisplayFrequency
                            });
                        }
                    }
                    i++;
                }
            }
            catch { }

            // Sort: highest width, highest refresh rate first
            list.Sort(delegate(DisplayResolutionItem a, DisplayResolutionItem b)
            {
                int cmp = b.Width.CompareTo(a.Width);
                if (cmp != 0) return cmp;
                return b.RefreshRate.CompareTo(a.RefreshRate);
            });

            return list;
        }
    }
}
