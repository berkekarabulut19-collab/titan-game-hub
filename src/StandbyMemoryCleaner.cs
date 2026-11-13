using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TitanGameHub
{
    public static class StandbyMemoryCleaner
    {
        [DllImport("ntdll.dll")]
        private static extern uint NtSetSystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, int BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public LUID Luid;
            public int Attributes;
        }

        private const int SystemMemoryListInformation = 80;
        private const int MemoryPurgeStandbyList = 4;
        private const int MemoryEmptyWorkingSets = 2;
        private const string SE_INCREASE_QUOTA_NAME = "SeIncreaseQuotaPrivilege";
        private const string SE_PROFILE_SINGLE_PROCESS_NAME = "SeProfileSingleProcessPrivilege";
        private const int SE_PRIVILEGE_ENABLED = 0x00000002;
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;

        public static bool PurgeStandbyMemory(out double freedMb)
        {
            freedMb = 0;
            Win32.MEMORYSTATUSEX before = new Win32.MEMORYSTATUSEX();
            Win32.GlobalMemoryStatusEx(before);

            EnablePrivilege(SE_INCREASE_QUOTA_NAME);
            EnablePrivilege(SE_PROFILE_SINGLE_PROCESS_NAME);

            // Empty Working Sets
            IntPtr emptyCmd = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(emptyCmd, MemoryEmptyWorkingSets);
            NtSetSystemInformation(SystemMemoryListInformation, emptyCmd, sizeof(int));
            Marshal.FreeHGlobal(emptyCmd);

            // Purge Standby List
            IntPtr purgeCmd = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(purgeCmd, MemoryPurgeStandbyList);
            uint status = NtSetSystemInformation(SystemMemoryListInformation, purgeCmd, sizeof(int));
            Marshal.FreeHGlobal(purgeCmd);

            Win32.MEMORYSTATUSEX after = new Win32.MEMORYSTATUSEX();
            Win32.GlobalMemoryStatusEx(after);

            if (after.ullAvailPhys > before.ullAvailPhys)
            {
                freedMb = (after.ullAvailPhys - before.ullAvailPhys) / (1024.0 * 1024.0);
            }
            return status == 0 || freedMb > 0;
        }

        private static bool EnablePrivilege(string privilegeName)
        {
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token))
                    return false;

                LUID luid;
                if (!LookupPrivilegeValue(null, privilegeName, out luid))
                    return false;

                TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES();
                tp.PrivilegeCount = 1;
                tp.Luid = luid;
                tp.Attributes = SE_PRIVILEGE_ENABLED;

                return AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (token != IntPtr.Zero) CloseHandle(token);
            }
        }
    }
}
