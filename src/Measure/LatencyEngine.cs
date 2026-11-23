// ArchTweaker 3.0: latency dashboard engine.
//  - CoreCounters: per-core % DPC time, % interrupt time, DPCs/s, interrupts/s from PDH. English counter
//    paths (PdhAddEnglishCounter) so it also works on Russian and other localized Windows.
//  - DriverTrace: a short kernel ETW trace (NT Kernel Logger, DPC + ISR flags) that times every DPC and ISR
//    and maps its routine address to the driver that owns it — the same method LatencyMon and xperf use.
// Read-only: nothing here changes the system.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ArchTweaker
{
    public class CoreSample { public int Index; public bool Fast; public double DpcPct, IsrPct, DpcsPerSec, IntsPerSec; }

    public sealed class CoreCounters : IDisposable
    {
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQuery(string src, IntPtr user, out IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounter(IntPtr q, string path, IntPtr user, out IntPtr c);
        [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr q);
        [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")] static extern int PdhGetFormattedCounterArray(IntPtr c, uint fmt, ref uint size, out uint count, IntPtr buf);
        const uint PDH_FMT_DOUBLE = 0x200, PDH_MORE_DATA = 0x800007D2;

        IntPtr q, cDpc, cIsr, cDpcs, cInts;
        public bool Ok { get; private set; }
        public string Error { get; private set; }

        public CoreCounters()
        {
            try
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out q) != 0) { Error = "PDH query failed"; return; }
                // 3.17.1: "Processor" (PerfOS) first; "Processor Information" (kernel counter set) when PerfOS is off or broken
                int st = 0;
                foreach (var set in new[] { "Processor", "Processor Information" })
                {
                    st = PdhAddEnglishCounter(q, @"\" + set + @"(*)\% DPC Time", IntPtr.Zero, out cDpc);
                    if (st == 0) st = PdhAddEnglishCounter(q, @"\" + set + @"(*)\% Interrupt Time", IntPtr.Zero, out cIsr);
                    if (st == 0) st = PdhAddEnglishCounter(q, @"\" + set + @"(*)\DPCs Queued/sec", IntPtr.Zero, out cDpcs);
                    if (st == 0) st = PdhAddEnglishCounter(q, @"\" + set + @"(*)\Interrupts/sec", IntPtr.Zero, out cInts);
                    if (st == 0) { Source = set; break; }
                    Log.W("PDH: {0} counters failed (0x{1:X8})", set, st);
                    PdhCloseQuery(q); q = IntPtr.Zero;
                    if (PdhOpenQuery(null, IntPtr.Zero, out q) != 0) break;
                }
                Ok = Source != null;
                if (!Ok)
                {
                    Error = PerfCounterFix.Disabled().Count > 0
                        ? "Something switched off the Windows performance counters on this PC. Click Fix counters to switch them on."
                        : "Processor counters are not available. Click Fix counters to rebuild them.";
                    return;
                }
                PdhCollectQueryData(q);   // first sample primes the rate counters
            }
            catch (Exception ex) { Ok = false; Error = "Processor counters are not available on this PC."; Log.W("PDH: {0}", ex.Message); }
        }
        public string Source { get; private set; }

        // Call about once a second.
        public List<CoreSample> Read()
        {
            var res = new List<CoreSample>();
            if (!Ok) return res;
            if (PdhCollectQueryData(q) != 0) return res;
            var dpc = Values(cDpc); var isr = Values(cIsr); var dps = Values(cDpcs); var ins = Values(cInts);
            ulong fast = HwProbe.Cpu.FastMask; bool hybrid = HwProbe.Cpu.Hybrid;
            foreach (var kv in dpc)
            {
                int idx; if (kv.Key == "_Total" || kv.Key.EndsWith("_Total", StringComparison.Ordinal) || !int.TryParse(kv.Key.Split(new[] { ',' }).Last(), out idx)) continue;
                double v;
                var s = new CoreSample { Index = idx, DpcPct = kv.Value, Fast = !hybrid || idx < 64 && (fast >> idx & 1) != 0 };
                if (isr.TryGetValue(kv.Key, out v)) s.IsrPct = v;
                if (dps.TryGetValue(kv.Key, out v)) s.DpcsPerSec = v;
                if (ins.TryGetValue(kv.Key, out v)) s.IntsPerSec = v;
                res.Add(s);
            }
            return res.OrderBy(x => x.Index).ToList();
        }

        Dictionary<string, double> Values(IntPtr c)
        {
            var d = new Dictionary<string, double>();
            uint size = 0, count;
            int st = PdhGetFormattedCounterArray(c, PDH_FMT_DOUBLE, ref size, out count, IntPtr.Zero);
            if ((uint)st != PDH_MORE_DATA || size == 0) return d;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(c, PDH_FMT_DOUBLE, ref size, out count, buf) != 0) return d;
                // PDH_FMT_COUNTERVALUE_ITEM_W: LPWSTR szName; PDH_FMT_COUNTERVALUE (8-aligned) { DWORD CStatus; double } → 24 bytes, value at 16 on x86 and x64
                const int item = 24;
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = new IntPtr(buf.ToInt64() + i * item);
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(p));
                    long bits = Marshal.ReadInt64(p, 16);
                    if (name != null) d[name] = BitConverter.Int64BitsToDouble(bits);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return d;
        }

        public void Dispose() { if (q != IntPtr.Zero) { PdhCloseQuery(q); q = IntPtr.Zero; } }

        // Average % DPC and % interrupt time over all cores for `ms` milliseconds (used by the Measure page).
        public static void Average(int ms, out double dpc, out double isr)
        {
            dpc = isr = -1;
            using (var c = new CoreCounters())
            {
                if (!c.Ok) return;
                double sd = 0, si = 0; int n = 0;
                for (int t = 0; t < Math.Max(1, ms / 500); t++)
                {
                    Thread.Sleep(500);
                    var r = c.Read(); if (r.Count == 0) continue;
                    sd += r.Average(x => x.DpcPct); si += r.Average(x => x.IsrPct); n++;
                }
                if (n > 0) { dpc = sd / n; isr = si / n; }
            }
        }
    }

    // 3.17.1: finds and repairs switched-off performance counters (debloated images often set
    // Services\<svc>\Performance\Disable Performance Counters = 1, which lodctr /R does not clear).
    public static class PerfCounterFix
    {
        const string Services = @"SYSTEM\CurrentControlSet\Services";
        public static List<string> Disabled()
        {
            var res = new List<string>();
            try
            {
                using (var b = Reg.Base(Hive.HKLM)) using (var svc = b.OpenSubKey(Services))
                {
                    if (svc == null) return res;
                    foreach (var n in svc.GetSubKeyNames())
                        try
                        {
                            using (var k = svc.OpenSubKey(n + @"\Performance"))
                            {
                                if (k == null) continue;
                                object v = k.GetValue("Disable Performance Counters");
                                if (v != null && Convert.ToInt64(v) != 0) res.Add(n);
                            }
                        }
                        catch { }
                }
            }
            catch { }
            return res;
        }

        public static void Run()
        {
            var off = Disabled();
            foreach (var n in off)
                try
                {
                    using (var b = Reg.Base(Hive.HKLM)) using (var k = b.OpenSubKey(Services + @"\" + n + @"\Performance", true))
                        if (k != null) k.DeleteValue("Disable Performance Counters", false);
                }
                catch (Exception ex) { Log.W("Could not switch on counters of {0}: {1}", n, ex.Message); }
            if (off.Count > 0) Log.W("Switched on performance counters: {0}", string.Join(", ", off.ToArray()));
            foreach (var dir in new[] { "System32", "SysWOW64" })
            {
                string r = Shell.Run("cmd.exe", "/c cd /d %SystemRoot%\\" + dir + " && lodctr /R", true, 120000).Trim();
                if (r.Length > 0) Log.W("lodctr /R ({0}): {1}", dir, r.Split('\n').Last().Trim());
            }
            foreach (var p in new[] { "PerfOS", "PerfProc", "PerfDisk", "PerfNet" }) Shell.Run("lodctr.exe", "/E:" + p, true, 30000);
            Shell.Run("cmd.exe", "/c %SystemRoot%\\System32\\wbem\\winmgmt.exe /resyncperf", true, 60000);
            using (var c = new CoreCounters())
                Log.W(c.Ok ? "Performance counters rebuilt. Processor counters work ({0})." : "Performance counters rebuilt, but processor counters still do not work. Restart Windows and try again.", c.Source);
        }
    }

    public class DriverStat
    {
        public string Driver; public int Dpcs, Isrs; public double DpcMaxUs, IsrMaxUs, DpcTotalUs, IsrTotalUs;
        public int Over500, Over1000;   // 3.8: DPCs / ISRs that took at least 500 µs / 1 ms
        public double MaxUs { get { return Math.Max(DpcMaxUs, IsrMaxUs); } }
    }

    public static class DriverTrace
    {
        // ---- native ETW ----
        static readonly Guid SystemTraceControlGuid = new Guid("9e814aad-3204-11d2-9a82-006008a86939");
        static readonly Guid PerfInfoGuid = new Guid("ce1dbfb4-137e-4da6-87b0-3f59aa102cbc");
        const string KernelLogger = "NT Kernel Logger";
        const uint EVENT_TRACE_FLAG_DPC = 0x20, EVENT_TRACE_FLAG_INTERRUPT = 0x40, EVENT_TRACE_REAL_TIME_MODE = 0x100, WNODE_FLAG_TRACED_GUID = 0x20000;
        const uint PROCESS_TRACE_MODE_REAL_TIME = 0x100, PROCESS_TRACE_MODE_RAW_TIMESTAMP = 0x1000, PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
        const uint EVENT_TRACE_CONTROL_STOP = 1;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "StartTraceW")] static extern uint StartTrace(out ulong handle, string name, IntPtr props);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "ControlTraceW")] static extern uint ControlTrace(ulong handle, string name, IntPtr props, uint code);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "OpenTraceW")] static extern ulong OpenTrace(ref EVENT_TRACE_LOGFILE log);
        [DllImport("advapi32.dll")] static extern uint ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll")] static extern uint CloseTrace(ulong handle);
        [DllImport("psapi.dll")] static extern bool EnumDeviceDrivers([Out] IntPtr[] bases, uint cb, out uint needed);
        [DllImport("psapi.dll", CharSet = CharSet.Unicode)] static extern uint GetDeviceDriverBaseNameW(IntPtr b, StringBuilder name, uint size);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void EventRecordCallback(IntPtr record);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint BufferCallback(IntPtr log);

        [StructLayout(LayoutKind.Sequential)]
        struct EVENT_TRACE_HEADER { public ushort Size, FieldTypeFlags; public uint Version, ThreadId, ProcessId; public long TimeStamp; public Guid Guid; public uint KernelTime, UserTime; }
        [StructLayout(LayoutKind.Sequential)]
        struct EVENT_TRACE { public EVENT_TRACE_HEADER Header; public uint InstanceId, ParentInstanceId; public Guid ParentGuid; public IntPtr MofData; public uint MofLength, ClientContext; }
        [StructLayout(LayoutKind.Sequential)]
        struct TRACE_LOGFILE_HEADER
        {
            public uint BufferSize, Version, ProviderVersion, NumberOfProcessors; public long EndTime;
            public uint TimerResolution, MaximumFileSize, LogFileMode, BuffersWritten;
            public Guid LogInstanceGuid; public IntPtr LoggerName, LogFileName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 172)] public byte[] TimeZone;
            public long BootTime, PerfFreq, StartTime; public uint ReservedFlags, BuffersLost;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct EVENT_TRACE_LOGFILE
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string LogFileName;
            [MarshalAs(UnmanagedType.LPWStr)] public string LoggerName;
            public long CurrentTime; public uint BuffersRead, ProcessTraceMode;
            public EVENT_TRACE CurrentEvent; public TRACE_LOGFILE_HEADER LogfileHeader;
            public BufferCallback BufferCallback; public uint BufferSize, Filled, EventsLost;
            public EventRecordCallback EventRecordCallback; public uint IsKernelTrace; public IntPtr Context;
        }

        // EVENT_TRACE_PROPERTIES + room for the logger name, built by hand (its size depends on pointer size)
        static IntPtr Props(out int size)
        {
            int nameOff = IntPtr.Size == 8 ? 120 : 116;
            size = nameOff + 1024;
            IntPtr p = Marshal.AllocHGlobal(size);
            for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);
            Marshal.WriteInt32(p, 0, size);                                         // Wnode.BufferSize
            Marshal.Copy(SystemTraceControlGuid.ToByteArray(), 0, new IntPtr(p.ToInt64() + 24), 16);   // Wnode.Guid
            Marshal.WriteInt32(p, 40, 1);                                           // Wnode.ClientContext = QPC clock
            Marshal.WriteInt32(p, 44, (int)WNODE_FLAG_TRACED_GUID);
            Marshal.WriteInt32(p, 48, 64);                                          // BufferSize (KB)
            Marshal.WriteInt32(p, 52, 16); Marshal.WriteInt32(p, 56, 64);           // Minimum / MaximumBuffers
            Marshal.WriteInt32(p, 64, (int)EVENT_TRACE_REAL_TIME_MODE);             // LogFileMode
            Marshal.WriteInt32(p, 68, 1);                                           // FlushTimer (s)
            Marshal.WriteInt32(p, 72, (int)(EVENT_TRACE_FLAG_DPC | EVENT_TRACE_FLAG_INTERRUPT));
            Marshal.WriteInt32(p, nameOff - 4, nameOff);                            // LoggerNameOffset
            return p;
        }

        // ---- driver address map ----
        class Mod { public long Base; public string Name; }
        static List<Mod> Drivers()
        {
            var l = new List<Mod>();
            Privilege.Enable("SeDebugPrivilege");   // 3.8: Windows 11 24H2+ hides kernel driver addresses without it
            try
            {
                uint need;
                EnumDeviceDrivers(null, 0, out need);
                var arr = new IntPtr[need / IntPtr.Size + 16];
                if (!EnumDeviceDrivers(arr, (uint)(arr.Length * IntPtr.Size), out need)) return l;
                for (int i = 0; i < need / IntPtr.Size; i++)
                {
                    if (arr[i] == IntPtr.Zero) continue;
                    var sb = new StringBuilder(260);
                    GetDeviceDriverBaseNameW(arr[i], sb, 260);
                    l.Add(new Mod { Base = arr[i].ToInt64(), Name = sb.Length > 0 ? sb.ToString() : "0x" + arr[i].ToInt64().ToString("X") });
                }
            }
            catch { }
            return l.OrderBy(m => (ulong)m.Base).ToList();
        }
        static string Owner(List<Mod> mods, long addr)
        {
            ulong a = (ulong)addr; string name = null;
            int lo = 0, hi = mods.Count - 1;
            while (lo <= hi) { int mid = (lo + hi) / 2; if ((ulong)mods[mid].Base <= a) { name = mods[mid].Name; lo = mid + 1; } else hi = mid - 1; }
            return name ?? "unknown";
        }

        // Runs a kernel trace for `seconds` and returns per-driver DPC/ISR timings, worst first.
        // error is set (and null returned) when the trace can't start.
        public static bool Busy;   // 3.8: the last Run found the kernel trace in use
        // 3.8: stops "NT Kernel Logger" (a leftover from a crashed scan, or another tool the user closed)
        public static bool StopKernelLogger()
        {
            int sz; IntPtr p = Props(out sz);
            try { return ControlTrace(0, KernelLogger, p, EVENT_TRACE_CONTROL_STOP) == 0; }
            catch { return false; }
            finally { Marshal.FreeHGlobal(p); }
        }
        public static List<DriverStat> Run(int seconds, out string error) { return Run(seconds, out error, null, null); }

        // 3.43: spike gets every DPC / ISR of at least 250 µs as it happens: its end time (raw QPC, the same clock as
        // Stopwatch.GetTimestamp and PresentMon --qpc_time), driver, length in µs and DPC (true) or ISR. The Stutter finder
        // lines these up with slow frames. stop ends the trace early when set.
        public static List<DriverStat> Run(int seconds, out string error, Action<long, string, double, bool> spike, ManualResetEvent stop)
        {
            error = null; Busy = false;
            int size; IntPtr props = Props(out size);
            var stats = new Dictionary<string, DriverStat>(StringComparer.OrdinalIgnoreCase);
            var mods = Drivers();
            double freq = Stopwatch.Frequency;
            ulong session = 0, trace = 0; bool started = false;
            var done = new ManualResetEvent(false);
            try
            {
                uint st = StartTrace(out session, KernelLogger, props);
                if (st == 183)   // ERROR_ALREADY_EXISTS: left over from a crashed run of ours, or another tool
                {
                    Busy = true;
                    error = "The Windows kernel trace is already in use (LatencyMon, xperf, Process Monitor or a previous scan). Close that tool and scan again.";
                    return null;
                }
                if (st != 0) { error = "Couldn't start the kernel trace (error " + st + "). It needs administrator rights."; return null; }
                started = true;

                EventRecordCallback cb = delegate (IntPtr rec)
                {
                    try
                    {
                        var g = new Guid(Bytes(rec, 24, 16));
                        if (g != PerfInfoGuid) return;
                        int opcode = Marshal.ReadByte(rec, 45);                // EVENT_DESCRIPTOR at 40: Id, Version, Channel, Level, Opcode
                        bool isDpc = opcode == 66 || opcode == 68 || opcode == 69, isIsr = opcode == 67 || opcode == 50;
                        if (!isDpc && !isIsr) return;
                        ushort flags = (ushort)Marshal.ReadInt16(rec, 4);
                        bool p64 = (flags & 0x40) != 0 || ((flags & 0x20) == 0 && IntPtr.Size == 8);   // 0x40 = 64-bit header, 0x20 = 32-bit
                        IntPtr data = Marshal.ReadIntPtr(rec, IntPtr.Size == 8 ? 96 : 92);
                        int len = (ushort)Marshal.ReadInt16(rec, 86);
                        if (data == IntPtr.Zero || len < (p64 ? 16 : 12)) return;
                        long initial = Marshal.ReadInt64(data, 0);
                        long routine = p64 ? Marshal.ReadInt64(data, 8) : (uint)Marshal.ReadInt32(data, 8);
                        long end = Marshal.ReadInt64(rec, 16);
                        double us = (end - initial) * 1e6 / freq;
                        if (us < 0 || us > 1e6) return;
                        string drv = Owner(mods, routine);
                        DriverStat s;
                        if (!stats.TryGetValue(drv, out s)) stats[drv] = s = new DriverStat { Driver = drv };
                        if (us >= 500) { s.Over500++; if (us >= 1000) s.Over1000++; }
                        if (spike != null && us >= 250) spike(end, drv, us, isDpc);
                        if (isDpc) { s.Dpcs++; s.DpcTotalUs += us; if (us > s.DpcMaxUs) s.DpcMaxUs = us; }
                        else { s.Isrs++; s.IsrTotalUs += us; if (us > s.IsrMaxUs) s.IsrMaxUs = us; }
                    }
                    catch { }
                };
                var log = new EVENT_TRACE_LOGFILE
                {
                    LoggerName = KernelLogger,
                    ProcessTraceMode = PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD | PROCESS_TRACE_MODE_RAW_TIMESTAMP,
                    EventRecordCallback = cb,
                    LogfileHeader = new TRACE_LOGFILE_HEADER { TimeZone = new byte[172] }
                };
                trace = OpenTrace(ref log);
                if (trace == ulong.MaxValue || trace == 0xFFFFFFFF) { trace = 0; error = "Couldn't read the kernel trace."; return null; }

                var stopper = new Thread(delegate ()
                {
                    if (stop != null) { if (WaitHandle.WaitAny(new WaitHandle[] { done, stop }, seconds * 1000) == 0) return; }
                    else if (done.WaitOne(seconds * 1000)) return;            // ProcessTrace already ended: don't touch the session
                    int sz2; IntPtr p2 = Props(out sz2);
                    try { ControlTrace(session, KernelLogger, p2, EVENT_TRACE_CONTROL_STOP); } finally { Marshal.FreeHGlobal(p2); }
                }) { IsBackground = true };
                stopper.Start();
                uint pt = ProcessTrace(new[] { trace }, 1, IntPtr.Zero, IntPtr.Zero);   // returns when the session stops
                done.Set();
                GC.KeepAlive(cb);
                if (pt != 0 && stats.Count == 0) { error = "Reading the kernel trace failed (error " + pt + ")."; return null; }
                return stats.Values.OrderByDescending(s => s.MaxUs).ToList();
            }
            catch (Exception ex) { error = ex.Message; return null; }
            finally
            {
                done.Set();
                if (trace != 0) CloseTrace(trace);
                // stop only the session this run started; never another tool's (LatencyMon, xperf, Process Monitor)
                if (started) { try { ControlTrace(session, KernelLogger, props, EVENT_TRACE_CONTROL_STOP); } catch { } }
                Marshal.FreeHGlobal(props);
            }
        }

        static byte[] Bytes(IntPtr p, int off, int n) { var b = new byte[n]; Marshal.Copy(new IntPtr(p.ToInt64() + off), b, 0, n); return b; }

        // LatencyMon's rule of thumb: a DPC or ISR over 500 µs can cause audio and frame hitches; over 1000 µs is bad.
        public static int Level(double us) { return us >= 1000 ? 2 : us >= 500 ? 1 : 0; }
    }
}
