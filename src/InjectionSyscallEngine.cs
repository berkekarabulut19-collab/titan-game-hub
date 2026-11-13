using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TitanGameHub
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Vector3
    {
        public float X;
        public float Y;
        public float Z;

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float DistanceTo(Vector3 other)
        {
            float dx = X - other.X;
            float dy = Y - other.Y;
            float dz = Z - other.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Matrix4x4
    {
        public float M11, M12, M13, M14;
        public float M21, M22, M23, M24;
        public float M31, M32, M33, M34;
        public float M41, M42, M43, M44;

        public bool WorldToScreen(Vector3 target, int screenWidth, int screenHeight, out float screenX, out float screenY)
        {
            screenX = 0;
            screenY = 0;

            float w = target.X * M14 + target.Y * M24 + target.Z * M34 + M44;
            if (w < 0.01f) return false;

            float x = target.X * M11 + target.Y * M21 + target.Z * M31 + M41;
            float y = target.X * M12 + target.Y * M22 + target.Z * M32 + M42;

            float invW = 1.0f / w;
            x *= invW;
            y *= invW;

            screenX = (screenWidth / 2.0f) + (0.5f * x * screenWidth + 0.5f);
            screenY = (screenHeight / 2.0f) - (0.5f * y * screenHeight + 0.5f);
            return true;
        }
    }

    public static class InjectionSyscallEngine
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        /// <summary>
        /// Phase 8 (v9.0): Hell's Gate / Halo's Gate Dynamic Syscall Number (SSN) Resolver
        /// & NTDLL Inline Hook Integrity Auditor + Callstack Gadget Scanner.
        /// </summary>
        public static string AuditSyscallAndGadgetTable()
        {
            try
            {
                IntPtr hNtdll = GetModuleHandle("ntdll.dll");
                if (hNtdll == IntPtr.Zero) return "[SYSCALL] ntdll.dll taban adresi alınamadı.";

                string[] criticalStubs = new string[]
                {
                    "NtOpenProcess",
                    "NtReadVirtualMemory",
                    "NtWriteVirtualMemory",
                    "NtAllocateVirtualMemory",
                    "NtProtectVirtualMemory",
                    "NtQueryVirtualMemory"
                };

                int cleanCount = 0;
                int hookedCount = 0;
                List<string> ssnList = new List<string>();

                foreach (string fn in criticalStubs)
                {
                    IntPtr pFn = GetProcAddress(hNtdll, fn);
                    if (pFn == IntPtr.Zero) continue;

                    byte[] stub = new byte[24];
                    Marshal.Copy(pFn, stub, 0, stub.Length);

                    // Standard x64 Windows Syscall Prologue:
                    // 4C 8B D1          mov r10, rcx
                    // B8 XX XX 00 00    mov eax, SSN
                    if (stub[0] == 0x4C && stub[1] == 0x8B && stub[2] == 0xD1 && stub[3] == 0xB8)
                    {
                        ushort ssn = BitConverter.ToUInt16(stub, 4);
                        cleanCount++;
                        ssnList.Add(string.Format("{0}=0x{1:X2}", fn.Substring(2, 6), ssn));
                    }
                    else
                    {
                        // Halo's Gate Neighbor Scan (+/- 32 byte stride per Nt* stub)
                        hookedCount++;
                        ushort resolvedSsn = ResolveHalosGateNeighborSsn(pFn);
                        ssnList.Add(string.Format("{0}=0x{1:X2}(Halo)", fn.Substring(2, 6), resolvedSsn));
                    }
                }

                IntPtr gadgetAddr = FindCallstackSpoofGadget();
                string gadgetHex = gadgetAddr != IntPtr.Zero ? ("0x" + gadgetAddr.ToInt64().ToString("X")) : "Standart";

                return string.Format(
                    "[SYSCALL SSN TABLOSU] Temiz Stub: {0}/{1} | Halo's Gate: {2} | Gadget: {3} | [{4}]",
                    cleanCount, criticalStubs.Length, hookedCount, gadgetHex, string.Join(", ", ssnList.ToArray()));
            }
            catch (Exception ex)
            {
                return "[SYSCALL] Denetim uyarısı: " + ex.Message;
            }
        }

        private static ushort ResolveHalosGateNeighborSsn(IntPtr hookedFnAddress)
        {
            try
            {
                for (int idx = 1; idx <= 16; idx++)
                {
                    // Check downward neighbor (+32 * idx)
                    IntPtr down = new IntPtr(hookedFnAddress.ToInt64() + (idx * 32));
                    byte[] buf = new byte[8];
                    Marshal.Copy(down, buf, 0, 8);
                    if (buf[0] == 0x4C && buf[1] == 0x8B && buf[2] == 0xD1 && buf[3] == 0xB8)
                    {
                        ushort neighborSsn = BitConverter.ToUInt16(buf, 4);
                        return (ushort)Math.Max(0, neighborSsn - idx);
                    }
                }
            }
            catch { }
            return 0;
        }

        private static IntPtr FindCallstackSpoofGadget()
        {
            try
            {
                IntPtr hKernel32 = GetModuleHandle("kernel32.dll");
                if (hKernel32 == IntPtr.Zero) return IntPtr.Zero;

                // Scan first 64KB of .text section for FF 23 (jmp QWORD PTR [rbx]) or FF E3 (jmp rbx)
                IntPtr textBase = new IntPtr(hKernel32.ToInt64() + 0x1000);
                byte[] code = new byte[32768];
                Marshal.Copy(textBase, code, 0, code.Length);

                for (int i = 0; i < code.Length - 2; i++)
                {
                    if (code[i] == 0xFF && (code[i + 1] == 0x23 || code[i + 1] == 0xE3))
                    {
                        return new IntPtr(textBase.ToInt64() + i);
                    }
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        public static bool InjectDll(IntPtr processHandle, string dllPath, out string error)
        {
            error = "Oyun bütünlüğü korunuyor. Harici DLL enjeksiyonu devre dışı.";
            return false;
        }

        public static bool SuspendProcess(IntPtr hProcess)
        {
            return true;
        }

        public static bool ResumeProcess(IntPtr hProcess)
        {
            return true;
        }

        public static T ReadStructure<T>(IntPtr hProcess, IntPtr address) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] buf = new byte[size];
            IntPtr read;
            if (Win32.ReadProcessMemory(hProcess, address, buf, size, out read))
            {
                GCHandle handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
                try
                {
                    return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
                }
                finally
                {
                    handle.Free();
                }
            }
            return default(T);
        }

        public static bool WriteStructure<T>(IntPtr hProcess, IntPtr address, T value) where T : struct
        {
            int size = Marshal.SizeOf(value);
            byte[] buf = new byte[size];
            GCHandle handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject();
                Marshal.StructureToPtr(value, ptr, false);
                IntPtr bytesWritten;
                bool ok = Win32.WriteProcessMemory(hProcess, address, buf, size, out bytesWritten);
                return ok;
            }
            catch
            {
                return false;
            }
            finally
            {
                handle.Free();
            }
        }
    }
}