using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TitanGameHub
{
    public class AudioDeviceItem
    {
        public int DeviceId { get; set; }
        public string DeviceName { get; set; }
        public int Channels { get; set; }
        public bool IsDefault { get; set; }

        public string DisplayText
        {
            get { return string.Format("{0} [{1} Ch]{2}", DeviceName, Channels, IsDefault ? " (Varsayılan)" : ""); }
        }
    }

    public static class PerGameAudioRouterService
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct WAVEOUTCAPS
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint dwFormats;
            public ushort wChannels;
            public ushort wReserved1;
            public uint dwSupport;
        }

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern uint waveOutGetNumDevs();

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern uint waveOutGetDevCaps(IntPtr uDeviceID, out WAVEOUTCAPS pwoc, uint cbwoc);

        public static List<AudioDeviceItem> GetAudioPlaybackDevices()
        {
            List<AudioDeviceItem> list = new List<AudioDeviceItem>();
            try
            {
                uint count = waveOutGetNumDevs();
                for (int i = 0; i < count; i++)
                {
                    WAVEOUTCAPS caps = new WAVEOUTCAPS();
                    if (waveOutGetDevCaps((IntPtr)i, out caps, (uint)Marshal.SizeOf(typeof(WAVEOUTCAPS))) == 0)
                    {
                        list.Add(new AudioDeviceItem
                        {
                            DeviceId = i,
                            DeviceName = caps.szPname,
                            Channels = caps.wChannels,
                            IsDefault = (i == 0)
                        });
                    }
                }
            }
            catch { }

            if (list.Count == 0)
            {
                list.Add(new AudioDeviceItem { DeviceId = 0, DeviceName = "Varsayılan Windows Ses Çıkışı", Channels = 2, IsDefault = true });
            }

            return list;
        }

        public static string GetAudioOptimizationTip()
        {
            return "İpucu: Windows Ses Denetim Masasında 'Loudness Equalization (Ses Eşitleme)' aktif edildiğinde patlama sesleri dengelenir ve oyunlardaki ayak sesleri belirginleşir.";
        }
    }
}
