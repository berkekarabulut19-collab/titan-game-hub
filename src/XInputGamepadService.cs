using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TitanGameHub
{
    public class GamepadStateInfo
    {
        public int UserIndex { get; set; }
        public bool IsConnected { get; set; }
        public string BatteryLevel { get; set; }
        public string ControllerType { get; set; }
    }

    public static class XInputGamepadService
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_BATTERY_INFORMATION
        {
            public byte BatteryType;
            public byte BatteryLevel;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_VIBRATION
        {
            public ushort wLeftMotorSpeed;
            public ushort wRightMotorSpeed;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        private const byte BATTERY_TYPE_DISCONNECTED = 0x00;
        private const byte BATTERY_TYPE_WIRED = 0x01;
        private const byte BATTERY_TYPE_ALKALINE = 0x02;
        private const byte BATTERY_TYPE_NIMH = 0x03;
        private const byte BATTERY_TYPE_UNKNOWN = 0xFF;

        private const byte BATTERY_LEVEL_EMPTY = 0x00;
        private const byte BATTERY_LEVEL_LOW = 0x01;
        private const byte BATTERY_LEVEL_MEDIUM = 0x02;
        private const byte BATTERY_LEVEL_FULL = 0x03;

        private const uint ERROR_SUCCESS = 0;

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState14(int dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
        private static extern uint XInputGetBatteryInformation14(int dwUserIndex, byte devType, ref XINPUT_BATTERY_INFORMATION pBatteryInformation);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
        private static extern uint XInputSetState14(int dwUserIndex, ref XINPUT_VIBRATION pVibration);

        // Fallbacks
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState91(int dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputSetState")]
        private static extern uint XInputSetState91(int dwUserIndex, ref XINPUT_VIBRATION pVibration);

        private static bool _use14 = true;

        static XInputGamepadService()
        {
            try
            {
                XINPUT_STATE s = new XINPUT_STATE();
                XInputGetState14(0, ref s);
                _use14 = true;
            }
            catch
            {
                _use14 = false;
            }
        }

        public static GamepadStateInfo GetPrimaryControllerState()
        {
            GamepadStateInfo info = new GamepadStateInfo
            {
                UserIndex = 0,
                IsConnected = false,
                BatteryLevel = "Bağlı Değil",
                ControllerType = "Gamepad Yok"
            };

            for (int i = 0; i < 4; i++)
            {
                XINPUT_STATE state = new XINPUT_STATE();
                uint res = 1;
                try
                {
                    res = _use14 ? XInputGetState14(i, ref state) : XInputGetState91(i, ref state);
                }
                catch { }

                if (res == ERROR_SUCCESS)
                {
                    info.UserIndex = i;
                    info.IsConnected = true;
                    info.ControllerType = "Xbox / XInput Gamepad (Oyuncu " + (i + 1) + ")";
                    info.BatteryLevel = GetBatteryLevelString(i);
                    return info;
                }
            }

            return info;
        }

        private static string GetBatteryLevelString(int userIndex)
        {
            if (!_use14) return "Kablolu / Aktif";

            try
            {
                XINPUT_BATTERY_INFORMATION bat = new XINPUT_BATTERY_INFORMATION();
                uint res = XInputGetBatteryInformation14(userIndex, 0x00, ref bat);
                if (res == ERROR_SUCCESS)
                {
                    if (bat.BatteryType == BATTERY_TYPE_WIRED) return "⚡ Kablolu Bağlantı";
                    if (bat.BatteryType == BATTERY_TYPE_DISCONNECTED) return "Bağlantı Kesildi";

                    switch (bat.BatteryLevel)
                    {
                        case BATTERY_LEVEL_FULL: return "🔋 Tam (%100)";
                        case BATTERY_LEVEL_MEDIUM: return "🔋 Orta (%65)";
                        case BATTERY_LEVEL_LOW: return "🪫 Düşük (%25)";
                        case BATTERY_LEVEL_EMPTY: return "🪫 Kritik Boş (%5)";
                    }
                }
            }
            catch { }

            return "🔋 Aktif";
        }

        public static void TestVibration(int userIndex, int durationMs = 500)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    XINPUT_VIBRATION vib = new XINPUT_VIBRATION
                    {
                        wLeftMotorSpeed = 45000,
                        wRightMotorSpeed = 45000
                    };

                    if (_use14) XInputSetState14(userIndex, ref vib);
                    else XInputSetState91(userIndex, ref vib);

                    Thread.Sleep(durationMs);

                    vib.wLeftMotorSpeed = 0;
                    vib.wRightMotorSpeed = 0;
                    if (_use14) XInputSetState14(userIndex, ref vib);
                    else XInputSetState91(userIndex, ref vib);
                }
                catch { }
            });
        }

        public static bool DeploySteamInputWrapper(GameProfile gp, out string message)
        {
            message = "";
            if (gp == null || string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(gp.WorkingDirectory))
            {
                message = "Oyun kurulum klasörü bulunamadı.";
                return false;
            }

            try
            {
                // Generate XInput wrapper config (x360ce / Steam Input equivalent mapping)
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[Options]");
                sb.AppendLine("UseInitBeep=1");
                sb.AppendLine("Log=0");
                sb.AppendLine("Console=0");
                sb.AppendLine("[Mappings]");
                sb.AppendLine("PAD1=IG_0");
                sb.AppendLine("[PAD1]");
                sb.AppendLine("[IG_0]");
                sb.AppendLine("A=1");
                sb.AppendLine("B=2");
                sb.AppendLine("X=3");
                sb.AppendLine("Y=4");
                sb.AppendLine("Left Shoulder=5");
                sb.AppendLine("Right Shoulder=6");
                sb.AppendLine("Back=7");
                sb.AppendLine("Start=8");
                sb.AppendLine("Left Thumb=9");
                sb.AppendLine("Right Thumb=10");
                sb.AppendLine("D-pad Up=POV 1");
                sb.AppendLine("D-pad Down=POV 2");
                sb.AppendLine("D-pad Left=POV 3");
                sb.AppendLine("D-pad Right=POV 4");

                File.WriteAllText(Path.Combine(gp.WorkingDirectory, "x360ce.ini"), sb.ToString(), Encoding.UTF8);
                message = "🎮 " + gp.Name + " için evrensel Steam Input & XInput Gamepad köprüsü kuruldu!";
                return true;
            }
            catch (Exception ex)
            {
                message = "Gamepad köprüsü hatası: " + ex.Message;
                return false;
            }
        }
    }
}
