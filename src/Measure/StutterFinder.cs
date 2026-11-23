// 3.43: Stutter finder. While a game runs, it records four things on one clock (QPC):
//   1. every frame (PresentMon --qpc_time),
//   2. every driver DPC / ISR of 250 µs or more (the kernel trace the Latency page uses),
//   3. every program that starts,
//   4. total CPU and disk load, and the busiest program, every 250 ms.
// Then it lines each slow frame up with what happened at that moment and names the most likely cause in plain words:
// "Frame of 48 ms at 0:41.2: the NVIDIA graphics driver held a CPU for 2.1 ms." Analyze() is pure, so tests cover it.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace ArchTweaker
{
    public static class StutterFinder
    {
        public enum Cause { Driver, Program, Disk, Cpu, Game }

        public sealed class FrameMark { public long Qpc; public double Ms; }
        public sealed class DpcMark { public long Qpc; public string Driver; public double Us; public bool Dpc; }
        public sealed class ProcMark { public long Qpc; public string Name; }
        public sealed class LoadMark { public long Qpc; public double CpuPct, DiskPct; public string Top; public double TopPct; }

        public sealed class Stutter
        {
            public double AtSec, Ms;
            public Cause Kind;
            public string Who;      // driver file, program name or busiest program; null for Game
            public double Amount;   // DPC/ISR µs, or load %
            public string Text;     // plain-language explanation, already translated
        }

        public sealed class Report
        {
            public string Game; public int Frames; public double Secs, MedianMs, ThresholdMs;
            public List<Stutter> Stutters = new List<Stutter>();
            public string Error, TraceError;
            public int Count(Cause c) { return Stutters.Count(s => s.Kind == c); }
            // the cause behind most stutters (ties: the order of Cause), and its most frequent driver / program
            public Cause Main { get { return Stutters.GroupBy(s => s.Kind).OrderByDescending(g => g.Count()).ThenBy(g => (int)g.Key).Select(g => g.Key).FirstOrDefault(); } }
            public string MainWho(Cause c)
            {
                return Stutters.Where(s => s.Kind == c && s.Who != null).GroupBy(s => s.Who, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            }
        }

        // ---- analysis (pure) ----
        public const double DriverUs = 500;       // LatencyMon's line: at or over 500 µs a DPC/ISR can cost a frame
        public const double DiskPct = 80, CpuPct = 90;
        public const double StartLeadSec = 1.0;   // a program that started up to 1 s before the slow frame counts

        // A frame is slow when it takes 2.5 × the median frame time and at least 8 ms more than it: at 240 FPS (4.2 ms)
        // that is 12.2 ms, at 60 FPS (16.7 ms) 41.7 ms. The same 2.5 × rule as the Benchmark's stutter count, plus a floor
        // so very high frame rates don't count tiny wobbles.
        public static double Threshold(double medianMs) { return Math.Max(medianMs * 2.5, medianMs + 8); }

        public static Report Analyze(List<FrameMark> frames, List<DpcMark> dpcs, List<ProcMark> procs, List<LoadMark> loads, double qpcFreq, long qpcStart)
        {
            var r = new Report();
            frames = (frames ?? new List<FrameMark>()).Where(f => f.Ms > 0 && f.Ms < 5000).OrderBy(f => f.Qpc).ToList();
            dpcs = dpcs ?? new List<DpcMark>(); procs = procs ?? new List<ProcMark>(); loads = loads ?? new List<LoadMark>();
            r.Frames = frames.Count;
            if (frames.Count < 10) return r;
            var sorted = frames.Select(f => f.Ms).OrderBy(x => x).ToList();
            r.MedianMs = sorted[sorted.Count / 2];
            r.ThresholdMs = Threshold(r.MedianMs);
            r.Secs = (frames[frames.Count - 1].Qpc - frames[0].Qpc) / qpcFreq;
            Func<double, long> ticks = sec => (long)(sec * qpcFreq);
            foreach (var f in frames.Where(f => f.Ms >= r.ThresholdMs))
            {
                // the frame was late somewhere in the time it took, so look from its start (plus a little) to just after it
                long from = f.Qpc - ticks(f.Ms / 1000.0 + 0.030), to = f.Qpc + ticks(0.010);
                var s = new Stutter { AtSec = Math.Max(0, (f.Qpc - qpcStart) / qpcFreq), Ms = f.Ms, Kind = Cause.Game };
                var d = dpcs.Where(x => x.Qpc >= from && x.Qpc <= to && x.Us >= DriverUs).OrderByDescending(x => x.Us).FirstOrDefault();
                var p = procs.Where(x => x.Qpc >= from - ticks(StartLeadSec) && x.Qpc <= to).OrderByDescending(x => x.Qpc).FirstOrDefault();
                var l = loads.Where(x => Math.Abs(x.Qpc - f.Qpc) <= ticks(0.300)).OrderByDescending(x => Math.Max(x.DiskPct, x.CpuPct)).FirstOrDefault();
                if (d != null) { s.Kind = Cause.Driver; s.Who = d.Driver; s.Amount = d.Us; }
                else if (p != null) { s.Kind = Cause.Program; s.Who = p.Name; }
                else if (l != null && l.DiskPct >= DiskPct) { s.Kind = Cause.Disk; s.Amount = l.DiskPct; s.Who = l.Top; }
                else if (l != null && l.CpuPct >= CpuPct) { s.Kind = Cause.Cpu; s.Amount = l.CpuPct; s.Who = l.Top; }
                s.Text = Explain(s);
                r.Stutters.Add(s);
            }
            return r;
        }

        public static string DriverName(string file)
        {
            var a = DriverAdvice.For(file);
            return a != null ? L.T(a.What) + " (" + file + ")" : file;
        }

        public static string Explain(Stutter s)
        {
            switch (s.Kind)
            {
                case Cause.Driver: return L.F("{0} held a CPU for {1} ms.", DriverName(s.Who), (s.Amount / 1000.0).ToString("0.0", CultureInfo.CurrentCulture));
                case Cause.Program: return L.F("{0} started at that moment.", s.Who);
                case Cause.Disk: return s.Who != null ? L.F("The disk was {0} % busy (busiest program: {1}).", Math.Round(s.Amount), s.Who) : L.F("The disk was {0} % busy.", Math.Round(s.Amount));
                case Cause.Cpu: return s.Who != null ? L.F("The CPU was {0} % busy (busiest program besides the game: {1}).", Math.Round(s.Amount), s.Who) : L.F("The CPU was {0} % busy.", Math.Round(s.Amount));
                default: return L.T("Nothing outside the game happened: the game itself (loading, shader compiling, streaming).");
            }
        }

        // One or two sentences for the top of the page
        public static string Summary(Report r)
        {
            if (r.Error != null) return r.Error;
            if (r.Frames < 10) return L.T("PresentMon caught too few frames. Is the game in the foreground and showing frames?");
            string head = L.F("{0} slow frames in {1} s ({2} frames, slow = over {3} ms).", r.Stutters.Count, Math.Round(r.Secs), r.Frames, r.ThresholdMs.ToString("0.0", CultureInfo.CurrentCulture));
            if (r.Stutters.Count == 0) return head + " " + L.T("No stutter: the frame pacing was even.");
            var parts = new List<string>();
            foreach (Cause c in Enum.GetValues(typeof(Cause)))
            {
                int n = r.Count(c); if (n == 0) continue;
                string who = r.MainWho(c);
                switch (c)
                {
                    case Cause.Driver: parts.Add(L.F("{0} matched a driver delay (mostly {1})", n, DriverName(who))); break;
                    case Cause.Program: parts.Add(L.F("{0} a program starting (mostly {1})", n, who)); break;
                    case Cause.Disk: parts.Add(L.F("{0} a busy disk", n)); break;
                    case Cause.Cpu: parts.Add(L.F("{0} a busy CPU", n)); break;
                    default: parts.Add(L.F("{0} had no outside cause", n)); break;
                }
            }
            return head + " " + string.Join("; ", parts.ToArray()) + ".";
        }

        // What to do about the main cause
        public static string Advice(Report r)
        {
            if (r.Stutters.Count == 0) return null;
            string who = r.MainWho(r.Main);
            switch (r.Main)
            {
                case Cause.Driver:
                    var a = DriverAdvice.For(who);
                    return L.T("Most slow frames came with a long driver delay.") + " " + (a != null ? L.T(a.Try) : L.T("Update or reinstall that driver.")) + " " + L.T("The Latency page and the Interrupt tuner can check it.");
                case Cause.Program: return L.F("Programs that start during play cost frames. Check {0} on the Startup and Scheduled tasks pages.", who);
                case Cause.Disk: return L.T("The disk was busy when frames were slow: Windows Search indexing, Windows Update, an antivirus scan or the game loading.");
                case Cause.Cpu: return L.T("Other programs used most of the CPU. Close them while you play (browsers, launchers, recording software).");
                default: return L.T("These slow frames come from the game itself. Windows tweaks can't fix them. A game or driver update, or lower settings, can.");
            }
        }

        // ---- capture ----
        // Runs for `seconds` while the game plays. status gets short progress lines. Never throws.
        public static Report Run(string game, int seconds, Action<string> status)
        {
            var r = new Report { Game = game };
            try
            {
                if (string.IsNullOrEmpty(game)) { r.Error = L.T("Choose the game first. It must be running."); return r; }
                if (!PresentMon.Available) { r.Error = L.T("PresentMon not found. Click Choose PresentMon.exe… on the Benchmark page."); return r; }
                var dpcs = new List<DpcMark>(); var procs = new List<ProcMark>(); var loads = new List<LoadMark>();
                var stop = new ManualResetEvent(false);
                long start = Stopwatch.GetTimestamp();
                string traceErr = null;
                var trace = new Thread(delegate ()
                {
                    string e;
                    DriverTrace.Run(seconds + 4, out e, (q, drv, us, dpc) => { lock (dpcs) dpcs.Add(new DpcMark { Qpc = q, Driver = drv, Us = us, Dpc = dpc }); }, stop);
                    traceErr = e;
                }) { IsBackground = true, Name = "ArchTweaker stutter trace" };
                var sampler = new Thread(delegate () { Sample(game, procs, loads, stop); }) { IsBackground = true, Name = "ArchTweaker stutter sampler" };
                trace.Start(); sampler.Start();
                if (status != null) status(L.F("Recording {0} for {1} s. Keep playing…", game, seconds));
                Thread.Sleep(500);   // let the trace and the first load sample start before the frames
                long pmStart = 0; string err;
                var lines = PresentMon.CaptureLines(game, seconds, true, () => pmStart = Stopwatch.GetTimestamp(), out err);
                stop.Set();
                trace.Join(15000); sampler.Join(3000);
                if (lines == null) { r.Error = err; return r; }
                if (status != null) status(L.T("Matching slow frames with drivers, programs and load…"));
                var frames = Frames(lines, pmStart);
                List<DpcMark> d; lock (dpcs) d = dpcs.ToList();
                List<ProcMark> p; lock (procs) p = procs.ToList();
                List<LoadMark> l; lock (loads) l = loads.ToList();
                var res = Analyze(frames, d, p, l, Stopwatch.Frequency, frames.Count > 0 ? frames[0].Qpc : start);
                res.Game = game; res.TraceError = traceErr;
                Log.W("Stutter finder ({0}, {1} s): {2} frames, {3} slow, driver {4}, program {5}, disk {6}, CPU {7}, game {8}", game, seconds, res.Frames, res.Stutters.Count,
                    res.Count(Cause.Driver), res.Count(Cause.Program), res.Count(Cause.Disk), res.Count(Cause.Cpu), res.Count(Cause.Game));
                return res;
            }
            catch (Exception ex) { Log.W("Stutter finder: {0}", ex); r.Error = ex.Message; return r; }
        }

        // PresentMon lines → frames on the QPC clock. With --qpc_time the TimeInQPC column is already QPC; older PresentMon
        // writes TimeInSeconds from its start, which is placed after pmStart (accurate to a few ms).
        public static List<FrameMark> Frames(string[] lines, long pmStart)
        {
            var o = new List<FrameMark>();
            if (lines == null || lines.Length < 2) return o;
            var h = lines[0].Split(',').Select(x => x.Trim().Trim('"').ToLowerInvariant()).ToArray();
            int ft = Array.IndexOf(h, "msbetweenpresents"); if (ft < 0) ft = Array.IndexOf(h, "frametime"); if (ft < 0) ft = Array.IndexOf(h, "msbetweenappstart");
            int tq = Array.IndexOf(h, "timeinqpc"), ts = Array.IndexOf(h, "timeinseconds"), sc = Array.IndexOf(h, "swapchainaddress");
            if (ft < 0 || (tq < 0 && ts < 0)) return o;
            var rows = lines.Skip(1).Select(x => x.Split(',')).Where(f => f.Length > Math.Max(ft, Math.Max(tq, ts))).ToList();
            if (sc >= 0)
            {
                string main = rows.Where(f => sc < f.Length).GroupBy(f => f[sc]).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
                if (main != null) rows = rows.Where(f => sc < f.Length && f[sc] == main).ToList();
            }
            foreach (var f in rows)
            {
                double ms; if (!double.TryParse(f[ft].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out ms)) continue;
                long q;
                if (tq >= 0) { if (!long.TryParse(f[tq].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out q)) continue; }
                else { double sec; if (!double.TryParse(f[ts].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out sec)) continue; q = pmStart + (long)(sec * Stopwatch.Frequency); }
                o.Add(new FrameMark { Qpc = q, Ms = ms });
            }
            return o;
        }

        // New processes every 100 ms; CPU and disk load every 300 ms; the busiest other program every second.
        static void Sample(string game, List<ProcMark> procs, List<LoadMark> loads, ManualResetEvent stop)
        {
            PerformanceCounter cpu = null, disk = null;
            try { cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total"); cpu.NextValue(); } catch (Exception ex) { Log.W("Stutter finder: CPU counter: {0}", ex.Message); cpu = null; }
            try { disk = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total"); disk.NextValue(); } catch (Exception ex) { Log.W("Stutter finder: disk counter: {0}", ex.Message); disk = null; }
            try
            {
                var seen = new HashSet<int>(Pids());
                string gameName = System.IO.Path.GetFileNameWithoutExtension(game);
                var lastCpu = new Dictionary<int, TimeSpan>(); long lastTop = Stopwatch.GetTimestamp();
                string top = null; double topPct = 0;
                for (int i = 0; !stop.WaitOne(100); i++)
                {
                    foreach (var p in SafeProcesses())
                        using (p)
                        {
                            if (!seen.Add(p.Id)) continue;
                            string n; try { n = p.ProcessName + ".exe"; } catch { continue; }   // exited already
                            lock (procs) procs.Add(new ProcMark { Qpc = Stopwatch.GetTimestamp(), Name = n });
                        }
                    if (i % 10 == 9) TopProgram(gameName, lastCpu, ref lastTop, out top, out topPct);
                    if (i % 3 == 2)
                    {
                        double c = -1, d = -1;
                        try { if (cpu != null) c = cpu.NextValue(); } catch (Exception ex) { Log.W("Stutter finder: CPU counter: {0}", ex.Message); cpu = null; }
                        try { if (disk != null) d = 100 - disk.NextValue(); } catch (Exception ex) { Log.W("Stutter finder: disk counter: {0}", ex.Message); disk = null; }
                        lock (loads) loads.Add(new LoadMark { Qpc = Stopwatch.GetTimestamp(), CpuPct = c, DiskPct = d, Top = topPct >= 5 ? top : null, TopPct = topPct });
                    }
                }
            }
            catch (Exception ex) { Log.W("Stutter finder: sampler: {0}", ex.Message); }
            finally { if (cpu != null) cpu.Dispose(); if (disk != null) disk.Dispose(); }
        }

        static IEnumerable<int> Pids() { foreach (var p in SafeProcesses()) using (p) yield return p.Id; }
        static Process[] SafeProcesses() { try { return Process.GetProcesses(); } catch { return new Process[0]; } }

        // The program besides the game that used the most CPU since the last call, in % of all cores
        static void TopProgram(string gameName, Dictionary<int, TimeSpan> last, ref long lastAt, out string name, out double pct)
        {
            name = null; pct = 0;
            long now = Stopwatch.GetTimestamp(); double wall = (now - lastAt) / (double)Stopwatch.Frequency; lastAt = now;
            var next = new Dictionary<int, TimeSpan>();
            foreach (var p in SafeProcesses())
                using (p)
                {
                    try
                    {
                        if (p.Id == 0 || string.Equals(p.ProcessName, gameName, StringComparison.OrdinalIgnoreCase)) continue;
                        var t = p.TotalProcessorTime; next[p.Id] = t;
                        TimeSpan was;
                        if (wall > 0 && last.TryGetValue(p.Id, out was))
                        {
                            double v = (t - was).TotalSeconds / wall / Environment.ProcessorCount * 100;
                            if (v > pct) { pct = v; name = p.ProcessName + ".exe"; }
                        }
                    }
                    catch { }   // protected processes don't give their CPU time; they are simply left out
                }
            last.Clear(); foreach (var kv in next) last[kv.Key] = kv.Value;
        }
    }
}
