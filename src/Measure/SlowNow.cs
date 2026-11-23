// 3.43: "Why is my PC slow right now?" One click takes a 2-second snapshot (CPU, memory, disk, the busiest programs,
// power plan, battery saver, CPU speed) and answers in up to five plain sentences, each with the page that can fix it.
// Snapshot() reads the PC; Analyze() is pure, so tests cover the rules.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace ArchTweaker
{
    public static class SlowNow
    {
        public sealed class Proc { public string Name; public double CpuPct, Mb, IoMbs; }

        public sealed class Snap
        {
            public double CpuPct = -1, MemPct = -1, DiskPct = -1, PerfPct = -1;   // -1: not read
            public double FreeMb = -1;
            public List<Proc> Procs = new List<Proc>();
            public string Plan; public Guid PlanId;
            public bool OnBattery, BatterySaver;
        }

        public sealed class Finding
        {
            public int Level;        // 2 problem, 1 worth knowing, 0 fine
            public string Text;      // translated
            public string Page;      // page that fixes it, or null
        }

        public static readonly Guid PowerSaver = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a");

        // Windows jobs that slow a PC for a while and then stop by themselves
        static readonly string[] Update = { "TiWorker", "TrustedInstaller", "MoUsoCoreWorker", "wuauclt", "UsoClient", "WaaSMedicAgent", "SIHClient" };
        static readonly string[] Indexer = { "SearchIndexer", "SearchProtocolHost", "SearchFilterHost" };
        static readonly string[] Defender = { "MsMpEng", "MpDefenderCoreService", "NisSrv" };
        static readonly string[] Telemetry = { "CompatTelRunner", "DeviceCensus" };

        static bool Is(Proc p, string[] names) { return names.Any(n => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)); }
        static string Exe(Proc p) { return p.Name + ".exe"; }
        static string N0(double v) { return Math.Round(v).ToString(CultureInfo.CurrentCulture); }

        public static List<Finding> Analyze(Snap s)
        {
            var f = new List<Finding>();
            var byCpu = s.Procs.Where(p => p.CpuPct >= 1).OrderByDescending(p => p.CpuPct).ToList();
            var byIo = s.Procs.Where(p => p.IoMbs >= 1).OrderByDescending(p => p.IoMbs).ToList();
            var byMem = s.Procs.OrderByDescending(p => p.Mb).ToList();
            Func<string[], Proc> busy = names => s.Procs.Where(p => Is(p, names) && (p.CpuPct >= 5 || p.IoMbs >= 5)).OrderByDescending(p => p.CpuPct + p.IoMbs).FirstOrDefault();

            // Windows' own background jobs first: they explain a busy CPU or disk, and the fix is to wait
            var wu = busy(Update);
            if (wu != null) f.Add(new Finding { Level = 2, Text = L.F("Windows Update is working in the background ({0}). The PC is slower until it finishes. Let it finish, or pause updates in Windows Settings.", Exe(wu)) });
            var def = busy(Defender);
            if (def != null) f.Add(new Finding { Level = 2, Text = L.F("Microsoft Defender is scanning ({0} uses {1} % CPU). It stops by itself. Defender doesn't scan a game folder that is in its exclusions.", Exe(def), N0(def.CpuPct)) });
            var idx = busy(Indexer);
            if (idx != null) f.Add(new Finding { Level = 1, Text = L.F("Windows Search is indexing files ({0}). If you don't search inside files, its service can be turned off.", Exe(idx)), Page = Cat.TasksServices });
            var tel = busy(Telemetry);
            if (tel != null) f.Add(new Finding { Level = 1, Text = L.F("Windows telemetry is collecting data ({0}).", Exe(tel)), Page = Cat.Privacy });
            Func<Proc, bool> known = p => Is(p, Update) || Is(p, Defender) || Is(p, Indexer) || Is(p, Telemetry);

            if (s.CpuPct >= 85)
            {
                var top = byCpu.FirstOrDefault(p => !known(p));
                f.Add(new Finding { Level = 2, Page = "Startup",
                    Text = top != null ? L.F("The CPU is {0} % busy. {1} uses {2} % of it.", N0(s.CpuPct), Exe(top), N0(top.CpuPct)) : L.F("The CPU is {0} % busy.", N0(s.CpuPct)) });
            }
            if (s.MemPct >= 90)
            {
                var top = byMem.FirstOrDefault();
                f.Add(new Finding { Level = 2, Page = "Startup",
                    Text = top != null ? L.F("Memory is {0} % full ({1} MB free). {2} uses {3} MB. Close programs you don't need.", N0(s.MemPct), N0(s.FreeMb), Exe(top), N0(top.Mb))
                                       : L.F("Memory is {0} % full ({1} MB free). Close programs you don't need.", N0(s.MemPct), N0(s.FreeMb)) });
            }
            if (s.DiskPct >= 90)
            {
                var top = byIo.FirstOrDefault(p => !known(p));
                f.Add(new Finding { Level = 2, Page = "Disk space",
                    Text = top != null ? L.F("The disk is {0} % busy. {1} reads and writes {2} MB/s.", N0(s.DiskPct), Exe(top), N0(top.IoMbs)) : L.F("The disk is {0} % busy.", N0(s.DiskPct)) });
            }
            if (s.PerfPct >= 0 && s.PerfPct < 60 && s.CpuPct >= 30)
                f.Add(new Finding { Level = 2, Page = "Hardware audit", Text = L.F("The CPU runs at {0} % of its normal speed while it is busy: heat or a power limit holds it back.", N0(s.PerfPct)) });
            if (s.BatterySaver)
                f.Add(new Finding { Level = 2, Text = L.T("Battery saver is on. It slows the CPU and background apps. Turn it off in the notification area or Windows Settings.") });
            if (s.PlanId == PowerSaver)
                f.Add(new Finding { Level = 2, Page = Cat.PowerPlan, Text = L.F("The power plan is \"{0}\": it keeps the CPU slow on purpose.", s.Plan ?? "Power saver") });
            else if (s.OnBattery)
                f.Add(new Finding { Level = 1, Text = L.T("The PC runs on battery. Windows saves power and runs slower. Plug in the charger for full speed.") });

            if (!f.Any(x => x.Level == 2))
                f.Add(new Finding { Level = 0, Text = L.F("Nothing is slowing this PC down right now: CPU {0} %, memory {1} %, disk {2} %.", Pct(s.CpuPct), Pct(s.MemPct), Pct(s.DiskPct)) });
            return f.OrderByDescending(x => x.Level).Take(5).ToList();
        }
        static string Pct(double v) { return v < 0 ? "?" : N0(v); }

        // ---- reading the PC (about 2 s) ----
        [StructLayout(LayoutKind.Sequential)]
        class MEMORYSTATUSEX { public uint dwLength = 64; public uint dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);
        [StructLayout(LayoutKind.Sequential)] struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
        [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);
        [StructLayout(LayoutKind.Sequential)] struct IO_COUNTERS { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
        [DllImport("kernel32.dll")] static extern bool GetProcessIoCounters(IntPtr h, out IO_COUNTERS c);

        public static Snap Snapshot()
        {
            var s = new Snap();
            PerformanceCounter cpu = null, disk = null, perf = null;
            try { cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total"); cpu.NextValue(); } catch (Exception ex) { Log.W("Slow-now check: CPU counter: {0}", ex.Message); }
            try { disk = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total"); disk.NextValue(); } catch (Exception ex) { Log.W("Slow-now check: disk counter: {0}", ex.Message); }
            try { perf = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total"); perf.NextValue(); } catch (Exception ex) { Log.W("Slow-now check: CPU speed counter: {0}", ex.Message); }
            try
            {
                var before = ReadProcs();
                long t0 = Stopwatch.GetTimestamp();
                Thread.Sleep(2000);
                var after = ReadProcs();
                double wall = (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
                try { if (cpu != null) s.CpuPct = cpu.NextValue(); } catch (Exception ex) { Log.W("Slow-now check: CPU counter: {0}", ex.Message); }
                try { if (disk != null) s.DiskPct = Math.Max(0, 100 - disk.NextValue()); } catch (Exception ex) { Log.W("Slow-now check: disk counter: {0}", ex.Message); }
                try { if (perf != null) s.PerfPct = perf.NextValue(); } catch (Exception ex) { Log.W("Slow-now check: CPU speed counter: {0}", ex.Message); }
                int self = Process.GetCurrentProcess().Id;
                foreach (var kv in after)
                {
                    if (kv.Key == self || kv.Key == 0) continue;
                    Raw a = kv.Value, b;
                    var p = new Proc { Name = a.Name, Mb = a.Mb };
                    if (before.TryGetValue(kv.Key, out b) && wall > 0)
                    {
                        p.CpuPct = Math.Max(0, (a.Cpu - b.Cpu).TotalSeconds / wall / Environment.ProcessorCount * 100);
                        p.IoMbs = Math.Max(0, (a.Io - b.Io) / 1048576.0 / wall);
                    }
                    s.Procs.Add(p);
                }
            }
            finally { if (cpu != null) cpu.Dispose(); if (disk != null) disk.Dispose(); if (perf != null) perf.Dispose(); }
            try { var m = new MEMORYSTATUSEX(); if (GlobalMemoryStatusEx(m)) { s.MemPct = m.dwMemoryLoad; s.FreeMb = m.ullAvailPhys / 1048576.0; } } catch (Exception ex) { Log.W("Slow-now check: memory: {0}", ex.Message); }
            try { SYSTEM_POWER_STATUS ps; if (GetSystemPowerStatus(out ps)) { s.OnBattery = ps.ACLineStatus == 0; s.BatterySaver = (ps.SystemStatusFlag & 1) != 0; } } catch (Exception ex) { Log.W("Slow-now check: power status: {0}", ex.Message); }
            try { var a = PowerExplorer.Schemes().FirstOrDefault(x => x.Active); if (a != null) { s.Plan = a.Name; s.PlanId = a.Id; } } catch (Exception ex) { Log.W("Slow-now check: power plan: {0}", ex.Message); }
            return s;
        }

        sealed class Raw { public string Name; public TimeSpan Cpu; public double Mb; public ulong Io; }
        static Dictionary<int, Raw> ReadProcs()
        {
            var d = new Dictionary<int, Raw>();
            Process[] all; try { all = Process.GetProcesses(); } catch { return d; }
            foreach (var p in all)
                using (p)
                {
                    try
                    {
                        var r = new Raw { Name = p.ProcessName, Mb = p.PrivateMemorySize64 / 1048576.0 };
                        try { r.Cpu = p.TotalProcessorTime; } catch { }       // protected processes: no CPU time
                        try { IO_COUNTERS c; if (GetProcessIoCounters(p.Handle, out c)) r.Io = c.ReadBytes + c.WriteBytes; } catch { }   // or no I/O counters
                        d[p.Id] = r;
                    }
                    catch { }   // the process exited while being read
                }
            return d;
        }

        // Snapshot + rules, never throws
        public static List<Finding> Check()
        {
            try
            {
                var s = Snapshot();
                var f = Analyze(s);
                Log.W("Slow-now check: CPU {0} %, memory {1} %, disk {2} %, CPU speed {3} %, plan {4}: {5}", Pct(s.CpuPct), Pct(s.MemPct), Pct(s.DiskPct), Pct(s.PerfPct), s.Plan, string.Join(" | ", f.Select(x => x.Text).ToArray()));
                return f;
            }
            catch (Exception ex)
            {
                Log.W("Slow-now check: {0}", ex);
                return new List<Finding> { new Finding { Level = 1, Text = L.T("The check stopped. The log has the details.") } };
            }
        }
    }
}
