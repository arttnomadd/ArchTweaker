// ArchTweaker 3.18: one-click benchmark.
//  - Bench.Run: timer and Sleep(1) precision, scheduling jitter, % DPC/ISR time, a kernel driver trace and
//    (when a game runs) a PresentMon frame-time capture, in one go.
//  - BenchReport: a self-contained HTML report (cards with change against the previous run, frame-time chart,
//    percentiles, slowest drivers, specs, applied tweaks), saved in ProgramData\ArchTweaker\reports.
//  - BenchRuns: bench-runs.tsv, one line per run, for the page's cards, trend chart and run list.
//  - BenchmarkPage (Analyse → Benchmark).
// Read-only: a benchmark changes nothing on the system.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ArchTweaker
{
    public class BenchRun
    {
        public DateTime When = DateTime.Now;
        public string Game = "", Report = "", Plan = "", WorstDriver = "";
        public double TimerMs, SleepAvg, SleepP99, JitterUs, DpcPct = -1, IsrPct = -1, WorstDpcUs, WorstIsrUs;
        public double AvgFps = -1, P1Fps = -1, P01Fps = -1, P99Ms = -1;
        public int TraceSecs, Spikes, FrameSecs, Frames, Stutters = -1, Applied;
        public bool HasFrames { get { return AvgFps > 0; } }
        public double WorstUs { get { return Math.Max(WorstDpcUs, WorstIsrUs); } }
    }

    public static class BenchRuns
    {
        const int Keep = 30;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string File_ { get { return Path.Combine(Paths.Root, "bench-runs.tsv"); } }
        static string C(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
        static string D(double v) { return v.ToString("0.###", Inv); }

        public static string Line(BenchRun r)
        {
            return string.Join("\t", new[] {
                r.When.ToString("yyyy-MM-dd HH:mm:ss", Inv), C(r.Game), C(r.Report), C(r.Plan), C(r.WorstDriver),
                D(r.TimerMs), D(r.SleepAvg), D(r.SleepP99), D(r.JitterUs), D(r.DpcPct), D(r.IsrPct), D(r.WorstDpcUs), D(r.WorstIsrUs),
                D(r.AvgFps), D(r.P1Fps), D(r.P01Fps), D(r.P99Ms),
                r.TraceSecs.ToString(Inv), r.Spikes.ToString(Inv), r.FrameSecs.ToString(Inv), r.Frames.ToString(Inv), r.Stutters.ToString(Inv), r.Applied.ToString(Inv) });
        }
        public static BenchRun ParseLine(string l)
        {
            var p = (l ?? "").Split('\t'); if (p.Length < 23) return null;
            var r = new BenchRun();
            if (!DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm:ss", Inv, DateTimeStyles.None, out r.When)) return null;
            r.Game = p[1]; r.Report = p[2]; r.Plan = p[3]; r.WorstDriver = p[4];
            Func<int, double> d = i => { double v; return double.TryParse(p[i], NumberStyles.Float, Inv, out v) ? v : -1; };
            Func<int, int> n = i => { int v; return int.TryParse(p[i], NumberStyles.Integer, Inv, out v) ? v : -1; };
            r.TimerMs = d(5); r.SleepAvg = d(6); r.SleepP99 = d(7); r.JitterUs = d(8); r.DpcPct = d(9); r.IsrPct = d(10); r.WorstDpcUs = d(11); r.WorstIsrUs = d(12);
            r.AvgFps = d(13); r.P1Fps = d(14); r.P01Fps = d(15); r.P99Ms = d(16);
            r.TraceSecs = n(17); r.Spikes = n(18); r.FrameSecs = n(19); r.Frames = n(20); r.Stutters = n(21); r.Applied = n(22);
            return r;
        }
        public static void Save(BenchRun r)
        {
            try
            {
                var lines = File.Exists(File_) ? File.ReadAllLines(File_).ToList() : new List<string>();
                lines.Add(Line(r));
                if (lines.Count > Keep) lines = lines.Skip(lines.Count - Keep).ToList();
                File.WriteAllLines(File_, lines);
            }
            catch (Exception ex) { Log.W("Benchmark runs: {0}", ex.Message); }
        }
        // oldest first; a damaged line is skipped
        public static List<BenchRun> All()
        {
            var o = new List<BenchRun>();
            try { if (File.Exists(File_) && Secure.TrustedData(File_, "the benchmark runs")) foreach (var l in File.ReadAllLines(File_)) { var r = ParseLine(l); if (r != null) o.Add(r); } }
            catch (Exception ex) { Log.W("Benchmark runs: {0}", ex.Message); throw; }
            return o;
        }
        public static void Remove(BenchRun r)
        {
            try
            {
                if (!File.Exists(File_)) return;
                string stamp = r.When.ToString("yyyy-MM-dd HH:mm:ss", Inv);
                File.WriteAllLines(File_, File.ReadAllLines(File_).Where(l => !l.StartsWith(stamp + "\t", StringComparison.Ordinal)).ToArray());
                string rep = Bench.ReportIn(r.Report);   // 3.27.1: "reports\..\..\x" passed the old prefix check
                if (rep != null && File.Exists(rep)) File.Delete(rep);
            }
            catch (Exception ex) { Log.W("Benchmark runs: {0}", ex.Message); }
        }
        // the run before `r` with the same game (both without a game also match)
        public static BenchRun Previous(List<BenchRun> all, BenchRun r)
        {
            int i = all.IndexOf(r);
            for (int j = (i < 0 ? all.Count : i) - 1; j >= 0; j--)
                if (string.Equals(all[j].Game, r.Game, StringComparison.OrdinalIgnoreCase)) return all[j];
            return null;
        }
    }

    public class BenchResult
    {
        public BenchRun Run; public MetricSet M; public List<DriverStat> Drivers = new List<DriverStat>(); public FrameStats Frames;
        public string TraceError, FrameError;
    }

    public static class Bench
    {
        public static string ReportDir { get { string p = Path.Combine(Paths.Root, "reports"); Directory.CreateDirectory(p); return p; } }

        // Windows with a main window, without ArchTweaker and the Windows shell: the likely game is in here.
        static readonly HashSet<string> NotGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "ApplicationFrameHost", "TextInputHost", "SystemSettings", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost",
            "Taskmgr", "mmc", "cmd", "conhost", "powershell", "pwsh", "WindowsTerminal", "notepad", "ArchTweaker", "PresentMon", "LockApp", "dwm", "rundll32"
        };
        // A game drop-down: the "no game" choice first, then the running games; selects the first.
        public static void FillGameBox(System.Windows.Forms.ComboBox box, string noGame)
        {
            box.Items.Clear(); box.Items.Add(L.T(noGame));
            foreach (var g in Games()) box.Items.Add(g);
            box.SelectedIndex = 0;
        }
        // 3.27.1: a report path from a run file, only when it is an .html file directly in ReportDir; otherwise null
        public static string ReportIn(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                string full = Path.GetFullPath(path), dir = Path.GetFullPath(ReportDir).TrimEnd('\\', '/');
                string parent = Path.GetDirectoryName(full);
                if (parent == null || !string.Equals(parent.TrimEnd('\\', '/'), dir, StringComparison.OrdinalIgnoreCase)) return null;
                return full.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? full : null;
            }
            catch { return null; }
        }

        public static List<string> Games()
        {
            var o = new List<string>();
            int self = 0; try { self = Process.GetCurrentProcess().Id; } catch { }
            try
            {
                foreach (var p in Process.GetProcesses())
                    try
                    {
                        if (p.Id == self || NotGames.Contains(p.ProcessName) || p.MainWindowHandle == IntPtr.Zero) continue;
                        string n = p.ProcessName + ".exe";
                        if (!o.Contains(n, StringComparer.OrdinalIgnoreCase)) o.Add(n);
                    }
                    catch { }
                    finally { p.Dispose(); }
            }
            catch { }
            return o.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Steps run one after another, so the driver trace and the frame capture do not slow each other down.
        public static BenchResult Run(string game, int traceSecs, int frameSecs, Action<string> step)
        {
            var res = new BenchResult();
            var r = res.Run = new BenchRun { Game = game ?? "", TraceSecs = traceSecs, FrameSecs = string.IsNullOrEmpty(game) ? 0 : frameSecs };
            int steps = string.IsNullOrEmpty(game) ? 2 : 3;
            Action<int, string> say = (i, t) => { if (step != null) step(L.F("Step {0} of {1}: {2}", i, steps, t)); };

            say(1, L.T("timer, Sleep(1) and scheduling jitter"));
            res.M = Meter.Capture(300);
            if (res.M != null) { r.TimerMs = res.M.TimerMs; r.SleepAvg = res.M.Sleep1Avg; r.SleepP99 = res.M.Sleep1P99; r.JitterUs = res.M.JitterMaxUs; r.DpcPct = res.M.DpcPct; r.IsrPct = res.M.IsrPct; }

            say(2, L.F("driver DPC and ISR times ({0} s)", traceSecs));
            string err;
            var drv = DriverTrace.Run(traceSecs, out err);
            res.TraceError = err;
            if (drv != null)
            {
                res.Drivers = drv;
                var lr = LatencyRuns.From(drv, traceSecs);
                LatencyRuns.Save(lr);   // the Latency page shows this scan too
                r.WorstDpcUs = lr.DpcUs; r.WorstIsrUs = lr.IsrUs; r.Spikes = lr.Over500; r.WorstDriver = lr.Worst; r.Plan = lr.Plan;
            }
            if (string.IsNullOrEmpty(r.Plan)) try { var a = PowerExplorer.Schemes().FirstOrDefault(s => s.Active); if (a != null) r.Plan = a.Name ?? ""; } catch { }

            if (!string.IsNullOrEmpty(game))
            {
                say(3, L.F("frame times of {0} ({1} s)", game, frameSecs));
                string ferr;
                res.Frames = PresentMon.Capture(game, frameSecs, out ferr);
                res.FrameError = ferr;
                var f = res.Frames;
                if (f != null) { r.AvgFps = f.AvgFps; r.P1Fps = f.P1LowFps; r.P01Fps = f.P01LowFps; r.P99Ms = f.P99Ms; r.Frames = f.Frames; r.Stutters = f.Stutters; }
            }
            try { r.Applied = AppliedSet.Read().Count; } catch { }
            try { List<BenchRun> prevRuns; try { prevRuns = BenchRuns.All(); } catch { prevRuns = new List<BenchRun>(); } r.Report = BenchReport.Write(res, BenchRuns.Previous(prevRuns, r)); } catch (Exception ex) { Log.W("Benchmark report: {0}", ex.Message); }
            BenchRuns.Save(r);
            return res;
        }
    }

}
