using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TitanGameHub
{
    /// <summary>
    /// winmm.dll proxy dosyası Defender tarafından silindiğinde bile dlllist.txt / OnlineFix64.dll
    /// modüllerini oyun sürecine askıda (CREATE_SUSPENDED) enjekte ederek Steam lisans hatasını önler.
    /// </summary>
    public static class OnlineFixLaunchInjector
    {
        private const uint CREATE_SUSPENDED = 0x00000004;
        private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint PAGE_READWRITE = 0x04;
        private const uint INFINITE = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, UIntPtr nSize, out UIntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, UIntPtr dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out IntPtr lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        public static bool NeedsDirectInjection(string workingDir)
        {
            if (string.IsNullOrEmpty(workingDir) || !Directory.Exists(workingDir)) return false;
            bool hasWinmm = File.Exists(Path.Combine(workingDir, "winmm.dll"));
            if (hasWinmm) return false;
            return File.Exists(Path.Combine(workingDir, "OnlineFix64.dll")) ||
                   File.Exists(Path.Combine(workingDir, "dlllist.txt"));
        }

        public static List<string> ResolveDllLoadOrder(string workingDir)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(workingDir) || !Directory.Exists(workingDir)) return list;

            string dllListPath = Path.Combine(workingDir, "dlllist.txt");
            if (File.Exists(dllListPath))
            {
                try
                {
                    foreach (string raw in File.ReadAllLines(dllListPath, Encoding.UTF8))
                    {
                        string name = (raw ?? "").Trim();
                        if (string.IsNullOrEmpty(name) || name.StartsWith("#") || name.StartsWith(";")) continue;
                        string full = Path.IsPathRooted(name) ? name : Path.Combine(workingDir, name);
                        if (File.Exists(full) && !list.Contains(full))
                            list.Add(full);
                    }
                }
                catch { }
            }

            string of64 = Path.Combine(workingDir, "OnlineFix64.dll");
            if (File.Exists(of64) && !list.Contains(of64))
                list.Add(of64);

            return list;
        }

        public static Process LaunchWithOnlineFixInjection(string exePath, string workingDir, string appId)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;
            List<string> dlls = ResolveDllLoadOrder(workingDir);
            if (dlls.Count == 0) return null;

            string effectiveAppId = string.IsNullOrEmpty(appId) ? "480" : appId;
            try
            {
                Environment.SetEnvironmentVariable("SteamAppId", effectiveAppId);
                Environment.SetEnvironmentVariable("SteamGameId", effectiveAppId);
                Environment.SetEnvironmentVariable("SteamOverlayGameId", effectiveAppId);
            }
            catch { }

            STARTUPINFO si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
            PROCESS_INFORMATION pi;
            string cmdLine = "\"" + exePath + "\"";

            if (!CreateProcessW(exePath, cmdLine, IntPtr.Zero, IntPtr.Zero, false,
                CREATE_SUSPENDED, IntPtr.Zero, workingDir, ref si, out pi))
            {
                return null;
            }

            try
            {
                IntPtr k32 = GetModuleHandleW("kernel32.dll");
                IntPtr loadLibAddr = k32 != IntPtr.Zero ? GetProcAddress(k32, "LoadLibraryW") : IntPtr.Zero;
                if (loadLibAddr != IntPtr.Zero)
                {
                    foreach (string dll in dlls)
                    {
                        byte[] pathBytes = Encoding.Unicode.GetBytes(dll + "\0");
                        IntPtr remoteMem = VirtualAllocEx(pi.hProcess, IntPtr.Zero, (UIntPtr)pathBytes.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                        if (remoteMem == IntPtr.Zero) continue;

                        UIntPtr written;
                        if (WriteProcessMemory(pi.hProcess, remoteMem, pathBytes, (UIntPtr)pathBytes.Length, out written))
                        {
                            IntPtr tid;
                            IntPtr hRt = CreateRemoteThread(pi.hProcess, IntPtr.Zero, UIntPtr.Zero, loadLibAddr, remoteMem, 0, out tid);
                            if (hRt != IntPtr.Zero)
                            {
                                WaitForSingleObject(hRt, 5000);
                                CloseHandle(hRt);
                            }
                        }
                    }
                }

                ResumeThread(pi.hThread);
                try { return Process.GetProcessById(pi.dwProcessId); } catch { return null; }
            }
            catch
            {
                try { ResumeThread(pi.hThread); } catch { }
                return null;
            }
            finally
            {
                if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
                if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
            }
        }
    }
}
