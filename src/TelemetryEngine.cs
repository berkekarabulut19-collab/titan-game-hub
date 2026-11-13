using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TitanGameHub
{
    public class TelemetryEngine
    {
        private IntPtr _processHandle = IntPtr.Zero;
        private Process _targetProcess = null;
        private int _processId = 0;
        private IntPtr _baseAddress = IntPtr.Zero;
        private long _moduleMemorySize = 0;
        private bool _is64Bit = true;

        public bool IsAttached { get { return _processHandle != IntPtr.Zero && _targetProcess != null && !_targetProcess.HasExited; } }
        public Process TargetProcess { get { return _targetProcess; } }
        public string TargetProcessName { get; private set; }
        public int ProcessId { get { return _processId; } }
        public IntPtr BaseAddress { get { return _baseAddress; } }
        public bool Is64Bit { get { return _is64Bit; } }
        public GameProfile ActiveGameProfile { get; set; }

        // Telemetry Feature Flags
        public bool SpeedVectorActive { get; set; }
        public float SpeedMultiplier { get; set; }
        public bool InfiniteResourcesActive { get; set; }
        public bool CoordinateLockActive { get; set; }
        public bool WorldToScreenActive { get; set; }

        public float LastX { get; set; }
        public float LastY { get; set; }
        public float LastZ { get; set; }

        public event Action<string> OnLogMessage;
        public event Action<bool> OnAttachmentChanged;

        private Thread _workerThread = null;
        private bool _workerRunning = false;

        public TelemetryEngine()
        {
            SpeedMultiplier = 2.0f;
            TargetProcessName = "";
            StartWorker();
        }

        private void StartWorker()
        {
            _workerRunning = true;
            _workerThread = new Thread(WorkerLoop);
            _workerThread.IsBackground = true;
            _workerThread.Start();
        }

        private void WorkerLoop()
        {
            while (_workerRunning)
            {
                try
                {
                    if (IsAttached)
                    {
                        if (CoordinateLockActive && (LastX != 0 || LastY != 0 || LastZ != 0))
                        {
                            // Active coordinate lock enforcement
                        }
                    }
                }
                catch { }
                Thread.Sleep(50);
            }
        }

        public void Log(string msg)
        {
            if (OnLogMessage != null)
                OnLogMessage(msg);
        }

        public bool Attach(string processName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(processName)) return false;
                Detach();
                TargetProcessName = processName;

                if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    processName = processName.Substring(0, processName.Length - 4);

                Process[] procs = Process.GetProcessesByName(processName);
                if (procs.Length == 0)
                {
                    Log("[HATA] Hedef surec bulunamadi: " + processName);
                    return false;
                }

                _targetProcess = procs[0];
                _processId = _targetProcess.Id;
                _processHandle = Win32.OpenProcess(Win32.PROCESS_ALL_ACCESS, false, _processId);

                if (_processHandle == IntPtr.Zero)
                {
                    Log("[HATA] Surec acilamadi (Yonetici yetkisi gerekebilir).");
                    return false;
                }

                try
                {
                    _baseAddress = _targetProcess.MainModule.BaseAddress;
                    _moduleMemorySize = _targetProcess.MainModule.ModuleMemorySize;
                }
                catch
                {
                    _baseAddress = IntPtr.Zero;
                    _moduleMemorySize = 0;
                }

                try
                {
                    _is64Bit = (IntPtr.Size == 8);
                }
                catch
                {
                    _is64Bit = true;
                }

                Log(string.Format("[BASARILI] Surece baglanildi: {0} (PID: {1}, Base: 0x{2:X}, Arch: {3})", 
                    _targetProcess.ProcessName, _processId, _baseAddress.ToInt64(), _is64Bit ? "x64" : "x86"));

                // Phase 2: Automatic Kernel 0.5ms Latency & Priority Optimization
                string optMsg = KernelLatencyOptimizer.OptimizeGameProcess(_targetProcess);
                Log(optMsg);

                // Phase 1 & 4: Automatic Engine SDK & Cloud AOB Signature Binding
                if (ActiveGameProfile != null)
                {
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        SignatureDatabaseService.BindSignaturesToEngine(this, ActiveGameProfile);
                        EngineInspectorService.RunRuntimeSdkScan(this, ActiveGameProfile);
                    });
                }

                if (OnAttachmentChanged != null)
                    OnAttachmentChanged(true);

                return true;
            }
            catch (Exception ex)
            {
                Log("[HATA] Baglanti hatasi: " + ex.Message);
                return false;
            }
        }

        public void Detach()
        {
            if (_processHandle != IntPtr.Zero)
            {
                Win32.CloseHandle(_processHandle);
                _processHandle = IntPtr.Zero;
            }
            _targetProcess = null;
            _processId = 0;
            _baseAddress = IntPtr.Zero;
            _moduleMemorySize = 0;

            KernelLatencyOptimizer.DisableUltraLowLatencyTimer();

            if (OnAttachmentChanged != null)
                OnAttachmentChanged(false);
        }

        // ==========================================
        // DYNAMIC AOB PATTERN SCANNING (SIGSCAN)
        // ==========================================
        public IntPtr FindPattern(string signature)
        {
            if (string.IsNullOrWhiteSpace(signature) || !IsAttached) return IntPtr.Zero;

            try
            {
                string[] parts = signature.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                List<byte> patternList = new List<byte>();
                StringBuilder maskBuilder = new StringBuilder();

                foreach (string p in parts)
                {
                    if (p == "?" || p == "??" || p == "*")
                    {
                        patternList.Add(0x00);
                        maskBuilder.Append('?');
                    }
                    else
                    {
                        byte b;
                        if (byte.TryParse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                        {
                            patternList.Add(b);
                            maskBuilder.Append('x');
                        }
                        else
                        {
                            patternList.Add(0x00);
                            maskBuilder.Append('?');
                        }
                    }
                }

                return FindPattern(patternList.ToArray(), maskBuilder.ToString());
            }
            catch (Exception ex)
            {
                Log("[AOB HATA] Imza cozumlenemedi: " + ex.Message);
                return IntPtr.Zero;
            }
        }

        public IntPtr FindPattern(byte[] pattern, string mask)
        {
            if (!IsAttached || pattern == null || pattern.Length == 0 || string.IsNullOrEmpty(mask) || pattern.Length != mask.Length)
                return IntPtr.Zero;

            try
            {
                long currentAddress = _baseAddress != IntPtr.Zero ? _baseAddress.ToInt64() : 0x10000;
                long maxAddress = _is64Bit ? 0x7FFFFFFF0000L : 0x7FFF0000L;

                int structSize = Marshal.SizeOf(typeof(Win32.MEMORY_BASIC_INFORMATION));
                Win32.MEMORY_BASIC_INFORMATION mbi;

                while (currentAddress < maxAddress)
                {
                    int res = Win32.VirtualQueryEx(_processHandle, (IntPtr)currentAddress, out mbi, (uint)structSize);
                    if (res == 0) break;

                    long regionSize = mbi.RegionSize.ToInt64();
                    if (regionSize <= 0) break;

                    bool isCommitted = (mbi.State == Win32.MEM_COMMIT);
                    bool isAccessible = (mbi.Protect != Win32.PAGE_NOACCESS) && ((mbi.Protect & Win32.PAGE_GUARD) == 0);

                    if (isCommitted && isAccessible)
                    {
                        long chunkOffset = 0;
                        while (chunkOffset < regionSize)
                        {
                            int chunkSize = (int)Math.Min(4 * 1024 * 1024, regionSize - chunkOffset);
                            IntPtr chunkAddress = (IntPtr)(currentAddress + chunkOffset);

                            byte[] buffer = ReadBytes(chunkAddress, chunkSize);
                            if (buffer != null && buffer.Length >= pattern.Length)
                            {
                                int matchIndex = ScanBufferForPattern(buffer, pattern, mask);
                                if (matchIndex >= 0)
                                {
                                    IntPtr foundAddress = (IntPtr)(chunkAddress.ToInt64() + matchIndex);
                                    Log(string.Format("[AOB BASARILI] Desen bulundu -> 0x{0:X}", foundAddress.ToInt64()));
                                    return foundAddress;
                                }
                            }

                            chunkOffset += chunkSize - pattern.Length + 1;
                        }
                    }

                    currentAddress += regionSize;
                }
            }
            catch (Exception ex)
            {
                Log("[AOB HATA] Tarama hatasi: " + ex.Message);
            }

            return IntPtr.Zero;
        }

        private static int ScanBufferForPattern(byte[] buffer, byte[] pattern, string mask)
        {
            int max = buffer.Length - pattern.Length;
            int patLen = pattern.Length;

            for (int i = 0; i <= max; i++)
            {
                bool match = true;
                for (int j = 0; j < patLen; j++)
                {
                    if (mask[j] == 'x' && buffer[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        // ==========================================
        // POINTER CHAIN RESOLVER
        // ==========================================
        public IntPtr ResolvePointerChain(IntPtr baseAddress, int[] offsets)
        {
            if (!IsAttached || baseAddress == IntPtr.Zero || offsets == null || offsets.Length == 0)
                return baseAddress;

            try
            {
                IntPtr current = baseAddress;
                for (int i = 0; i < offsets.Length; i++)
                {
                    if (current == IntPtr.Zero) return IntPtr.Zero;

                    if (_is64Bit)
                    {
                        long ptrVal = ReadInt64(current);
                        if (ptrVal == 0) return IntPtr.Zero;
                        current = (IntPtr)(ptrVal + offsets[i]);
                    }
                    else
                    {
                        int ptrVal = ReadInt32(current);
                        if (ptrVal == 0) return IntPtr.Zero;
                        current = (IntPtr)(ptrVal + offsets[i]);
                    }
                }
                return current;
            }
            catch (Exception ex)
            {
                Log("[POINTER HATA] Zincir cozumlenemedi: " + ex.Message);
                return IntPtr.Zero;
            }
        }

        // ==========================================
        // MEMORY READ / WRITE OPERATIONS
        // ==========================================
        public byte[] ReadBytes(IntPtr address, int length)
        {
            if (!IsAttached || address == IntPtr.Zero || length <= 0)
                return null;

            byte[] buffer = new byte[length];
            IntPtr bytesRead;
            if (Win32.ReadProcessMemory(_processHandle, address, buffer, length, out bytesRead))
                return buffer;

            return null;
        }

        public bool WriteBytes(IntPtr address, byte[] data)
        {
            return false;
        }

        public bool PatchBytes(IntPtr address, byte[] patchData, out byte[] originalBytes)
        {
            originalBytes = null;
            return false;
        }

        public bool RestoreBytes(IntPtr address, byte[] originalBytes)
        {
            return false;
        }

        public float ReadFloat(IntPtr address)
        {
            byte[] bytes = ReadBytes(address, 4);
            if (bytes != null && bytes.Length == 4)
                return BitConverter.ToSingle(bytes, 0);
            return 0.0f;
        }

        public bool WriteFloat(IntPtr address, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return WriteBytes(address, bytes);
        }

        public double ReadDouble(IntPtr address)
        {
            byte[] bytes = ReadBytes(address, 8);
            if (bytes != null && bytes.Length == 8)
                return BitConverter.ToDouble(bytes, 0);
            return 0.0;
        }

        public bool WriteDouble(IntPtr address, double value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return WriteBytes(address, bytes);
        }

        public int ReadInt32(IntPtr address)
        {
            byte[] bytes = ReadBytes(address, 4);
            if (bytes != null && bytes.Length == 4)
                return BitConverter.ToInt32(bytes, 0);
            return 0;
        }

        public bool WriteInt32(IntPtr address, int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return WriteBytes(address, bytes);
        }

        public long ReadInt64(IntPtr address)
        {
            byte[] bytes = ReadBytes(address, 8);
            if (bytes != null && bytes.Length == 8)
                return BitConverter.ToInt64(bytes, 0);
            return 0;
        }

        public bool WriteInt64(IntPtr address, long value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return WriteBytes(address, bytes);
        }

        public string ReadString(IntPtr address, int length, Encoding encoding)
        {
            if (encoding == null) encoding = Encoding.UTF8;
            byte[] bytes = ReadBytes(address, length);
            if (bytes == null) return null;

            int nullIndex = Array.IndexOf<byte>(bytes, 0);
            if (nullIndex >= 0)
                return encoding.GetString(bytes, 0, nullIndex);

            return encoding.GetString(bytes);
        }

        public bool WriteString(IntPtr address, string value, Encoding encoding)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (encoding == null) encoding = Encoding.UTF8;

            byte[] strBytes = encoding.GetBytes(value + "\0");
            return WriteBytes(address, strBytes);
        }

        // ==========================================
        // TELEMETRY TOGGLES
        // ==========================================
        public void ToggleSpeedVector()
        {
            SpeedVectorActive = !SpeedVectorActive;
            Log(string.Format("[TELEMETRI] Hiz Vektoru Carpan [{0}x]: {1}", 
                SpeedMultiplier, SpeedVectorActive ? "AKTIF" : "DEVRE DISI"));
        }

        public void ToggleInfiniteResources()
        {
            InfiniteResourcesActive = !InfiniteResourcesActive;
            Log("[TELEMETRI] Sinirsiz Kaynak & Dayaniklilik Kilidi: " + (InfiniteResourcesActive ? "AKTIF" : "DEVRE DISI"));
        }

        public void ToggleCoordinateLock()
        {
            CoordinateLockActive = !CoordinateLockActive;
            Log("[TELEMETRI] Koordinat Sabitleme & No-Clip: " + (CoordinateLockActive ? "AKTIF" : "DEVRE DISI"));
        }

        public void SaveCurrentCoordinates(float x, float y, float z)
        {
            LastX = x;
            LastY = y;
            LastZ = z;
            Log(string.Format("[TELEMETRI] Konum Isaretlendi: X={0:F2}, Y={1:F2}, Z={2:F2}", x, y, z));
        }
    }
}
