// ArchTweaker 2.0: measurement loop. Turns "gains" into numbers measured on THIS machine.
// - timer resolution (current)
// - Sleep(1) precision: avg / p99 / max in ms
// - scheduling jitter: max gap seen by a tight sampling loop (an honest proxy, NOT kernel DPC latency)
// - optional PresentMon frametime capture → avg / 1% low / 0.1% low FPS (needs PresentMon.exe present)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace ArchTweaker
{
    public class MetricSet
    {
        public double TimerMs;
        public double Sleep1Avg, Sleep1P99, Sleep1Max;
        public double JitterMaxUs;
        public double DpcPct = -1, IsrPct = -1;   // 3.0: average % DPC / % interrupt time over all cores
        public DateTime When = DateTime.Now;

        public string Line()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "timer {0:0.000} ms | Sleep(1) avg {1:0.00} / p99 {2:0.00} / max {3:0.00} ms | sched jitter max {4:0.0} µs",
                TimerMs, Sleep1Avg, Sleep1P99, Sleep1Max, JitterMaxUs)
                + (DpcPct >= 0 ? string.Format(CultureInfo.InvariantCulture, " | DPC {0:0.00} % / ISR {1:0.00} %", DpcPct, IsrPct) : "");
        }
    }

    public static class Meter
    {
        public static MetricSet Capture(int sleepSamples)
        {
            var m = new MetricSet();
            try { m.TimerMs = TimerRes.CurrentMs(); } catch { }
            Sleep1(sleepSamples, m);
            m.JitterMaxUs = JitterMaxUs(250);
            try { CoreCounters.Average(2000, out m.DpcPct, out m.IsrPct); } catch { }
            return m;
        }

        static void Sleep1(int n, MetricSet m)
        {
            if (n < 20) n = 20;
            var ms = new double[n];
            var sw = new Stopwatch();
            for (int i = 0; i < n; i++)
            {
                sw.Restart(); Thread.Sleep(1); sw.Stop();
                ms[i] = sw.Elapsed.TotalMilliseconds;
            }
            Array.Sort(ms);
            m.Sleep1Avg = ms.Average();
            m.Sleep1P99 = ms[(int)Math.Min(n - 1, Math.Floor(n * 0.99))];
            m.Sleep1Max = ms[n - 1];
        }

        // Tight loop for ~windowMs; record the largest gap between consecutive Stopwatch reads.
        // This reflects how long the thread was descheduled — a usable before/after proxy, not LatencyMon.
        static double JitterMaxUs(int windowMs)
        {
            var sw = Stopwatch.StartNew();
            double last = 0, maxGap = 0;
            double freqUs = Stopwatch.Frequency / 1000000.0;
            long end = (long)(windowMs / 1000.0 * Stopwatch.Frequency);
            while (sw.ElapsedTicks < end)
            {
                double now = sw.ElapsedTicks;
                double gapUs = (now - last) / freqUs;
                if (gapUs > maxGap) maxGap = gapUs;
                last = now;
            }
            return maxGap;
        }

        public static string Compare(MetricSet a, MetricSet b)
        {
            Func<double, double, string> d = (x, y) =>
            {
                double delta = y - x; string sign = delta <= 0 ? "" : "+";
                return string.Format(CultureInfo.InvariantCulture, "{0}{1:0.00}", sign, delta);
            };
            return "Before:  " + a.Line() + "\r\nAfter:   " + b.Line() + "\r\n"
                 + "Delta:   Sleep(1) avg " + d(a.Sleep1Avg, b.Sleep1Avg) + " ms, max " + d(a.Sleep1Max, b.Sleep1Max)
                 + " ms, jitter " + d(a.JitterMaxUs, b.JitterMaxUs) + " µs"
                 + (a.DpcPct >= 0 && b.DpcPct >= 0 ? ", DPC " + d(a.DpcPct, b.DpcPct) + " %, ISR " + d(a.IsrPct, b.IsrPct) + " %" : "")
                 + " (lower is better).";
        }
    }

    // ---------- PresentMon frametime capture ----------
    public class FrameStats
    {
        public int Frames; public double AvgFps, P1LowFps, P01LowFps, AvgMs;
        // 3.18: the frame series and more numbers for the benchmark report
        public List<double> Ms = new List<double>();
        public double MedianMs, P99Ms, P999Ms, MaxMs, CpuBusyMs = -1, GpuBusyMs = -1, DisplayLatencyMs = -1;
        public int Stutters;   // frames longer than 2.5 times the median frame
        public string Column = "", Runtime = "", PresentMode = "";
        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} frames | avg {1:0.0} FPS | 1% low {2:0.0} | 0.1% low {3:0.0} (avg frametime {4:0.00} ms)",
                Frames, AvgFps, P1LowFps, P01LowFps, AvgMs) + (Stutters > 0 ? string.Format(CultureInfo.InvariantCulture, " | {0} stutters", Stutters) : "");
        }
    }

    public static class PresentMon
    {
        // Looked for next to the exe (PresentMon.exe, or a release name such as PresentMon-2.3.0-x64.exe),
        // then in ProgramData\ArchTweaker\bin (where "Choose PresentMon.exe" copies it).
        public static string Find()
        {
            string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            string beside = Path.Combine(dir, "PresentMon.exe");
            if (File.Exists(beside)) return beside;
            try
            {
                var rel = Directory.GetFiles(dir, "PresentMon*.exe").Where(f => !Path.GetFileName(f).StartsWith("PresentMonService", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f)).FirstOrDefault();
                if (rel != null) return rel;
            }
            catch { }
            string bin = BinPath;
            if (Secure.TrustedData(bin, "PresentMon in ArchTweaker's bin folder")) return bin;
            // 3.2.1: never a bare name: it would be found through PATH and the working folder and run elevated
            return null;
        }
        public static string BinPath { get { return Path.Combine(Paths.Root, @"bin\PresentMon.exe"); } }

        public static bool Available
        {
            get
            {
                try { string f = Find(); return f != null && File.Exists(f); } catch { return false; }
            }
        }

        // 3.18: "PresentMon 2.3.0 (C:\...\PresentMon.exe)" or null
        public static string Describe()
        {
            try
            {
                string f = Find(); if (f == null) return null;
                var v = FileVersionInfo.GetVersionInfo(f);
                string ver = (v.ProductVersion ?? v.FileVersion ?? "").Trim();
                return "PresentMon" + (ver.Length > 0 ? " " + ver : "") + " (" + f + ")";
            }
            catch { return null; }
        }

        // 3.18: copies a PresentMon console exe the user picked into ProgramData\ArchTweaker\bin. Null on success, else the reason.
        public static string Import(string src)
        {
            try
            {
                if (!File.Exists(src)) return "The file does not exist.";
                var head = new byte[2];
                using (var fs = File.OpenRead(src)) if (fs.Read(head, 0, 2) != 2 || head[0] != 'M' || head[1] != 'Z') return "This file is not a program.";
                var v = FileVersionInfo.GetVersionInfo(src);
                string names = (v.ProductName ?? "") + " " + (v.FileDescription ?? "") + " " + Path.GetFileName(src);
                if (names.IndexOf("PresentMon", StringComparison.OrdinalIgnoreCase) < 0) return "This file is not PresentMon.";
                if (names.IndexOf("Service", StringComparison.OrdinalIgnoreCase) >= 0 || names.IndexOf("Capture", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Choose the PresentMon console program (PresentMon-x.y.z-x64.exe), not the PresentMon app or service.";
                string dst = BinPath;
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(src, dst, true);
                Log.W("PresentMon copied to {0}", dst);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // Capture `seconds` of presents for the given process name (e.g. "game.exe"). Returns null + reason on failure.
        public static FrameStats Capture(string processName, int seconds, out string error)
        {
            var lines = CaptureLines(processName, seconds, false, null, out error);
            if (lines == null) return null;
            var fs = Parse(lines);
            if (fs == null || fs.Frames < 5) { error = "Not enough frames captured for " + processName + "."; return null; }
            return fs;
        }

        // 3.43: the raw CSV lines. qpc adds --qpc_time (PresentMon 2.x): TimeInQPC is then on the Stopwatch clock.
        // started is called just before PresentMon runs, so frames can be placed in time when the QPC column is missing.
        public static string[] CaptureLines(string processName, int seconds, bool qpc, Action started, out string error)
        {
            error = null;
            try
            {
                if (processName.IndexOf('"') >= 0) { error = "Invalid process name."; return null; }
                if (seconds < 1) seconds = 1; if (seconds > 600) seconds = 600;
                string exe = Find();
                if (exe == null) { error = "PresentMon.exe not found next to ArchTweaker.exe."; return null; }
                string csv = Path.Combine(Path.GetTempPath(), "at_presentmon_" + DateTime.Now.Ticks + ".csv");
                try
                {
                    // 3.18: PresentMon 2.x and newer use double-dash options; 1.x needs -no_top. Try the new form first.
                    string q = "\"" + processName + "\"";
                    string[] forms = {
                        "--process_name " + q + " --timed " + seconds + " --terminate_after_timed --no_console_stats --stop_existing_session --session_name ArchTweaker" + (qpc ? " --qpc_time" : "") + " --output_file \"" + csv + "\"",
                        "-process_name " + q + " -timed " + seconds + " -terminate_after_timed -no_top -output_file \"" + csv + "\"" };
                    string outp = "";
                    foreach (var args in forms)
                    {
                        if (started != null) started();
                        outp = Shell.Run(exe, args, true, (seconds + 20) * 1000);   // hard timeout so a stuck PresentMon can't hang the UI
                        if (File.Exists(csv)) break;
                    }
                    if (!File.Exists(csv))
                    {
                        string hint = outp != null && outp.Trim().Length > 0 ? outp.Trim().Split('\n')[0].Trim() : "";
                        error = "PresentMon produced no output (is PresentMon.exe present, and the game running?)" + (hint.Length > 0 ? " — " + hint : "");
                        return null;
                    }
                    return File.ReadAllLines(csv);
                }
                finally { try { File.Delete(csv); } catch { } }
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        // 3.18: reads PresentMon 1.x (MsBetweenPresents), 2.x/3.x (FrameTime, MsBetweenAppStart) CSV. Keeps the
        // swap chain with the most frames, so an overlay or a launcher window in the same process does not mix in.
        public static FrameStats Parse(string[] lines)
        {
            if (lines == null || lines.Length < 2) return null;
            var header = lines[0].Split(',').Select(h => h.Trim().Trim('"').ToLowerInvariant()).ToArray();
            Func<string[], int> col = names => { foreach (var n in names) { int i = Array.IndexOf(header, n); if (i >= 0) return i; } return -1; };
            int ft = col(new[] { "msbetweenpresents", "frametime", "msbetweenappstart" });
            if (ft < 0) return null;
            int sc = col(new[] { "swapchainaddress" }), cpu = col(new[] { "cpubusy", "mscpubusy" }), gpu = col(new[] { "gpubusy", "msgpubusy" });
            int disp = col(new[] { "displaylatency", "msdisplaylatency" }), rt = col(new[] { "runtime", "presentruntime" }), pm = col(new[] { "presentmode" });
            var rows = new List<string[]>();
            for (int r = 1; r < lines.Length; r++) { var f = lines[r].Split(','); if (ft < f.Length) rows.Add(f); }
            if (sc >= 0 && rows.Count > 0)
            {
                string main = rows.Where(f => sc < f.Length).GroupBy(f => f[sc]).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
                if (main != null) rows = rows.Where(f => sc < f.Length && f[sc] == main).ToList();
            }
            Func<string[], int, double> num = (f, i) =>
            {
                double v; return i >= 0 && i < f.Length && double.TryParse(f[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
            };
            var fs = new FrameStats { Column = header[ft] };
            double sc1 = 0, sg = 0, sd = 0; int nc = 0, ng = 0, nd = 0;
            foreach (var f in rows)
            {
                double v = num(f, ft); if (double.IsNaN(v) || v <= 0 || v > 5000) continue;
                fs.Ms.Add(v);
                double c = num(f, cpu); if (!double.IsNaN(c)) { sc1 += c; nc++; }
                double g = num(f, gpu); if (!double.IsNaN(g)) { sg += g; ng++; }
                double d = num(f, disp); if (!double.IsNaN(d) && d > 0) { sd += d; nd++; }
            }
            if (fs.Ms.Count < 5) return null;
            if (nc > 0) fs.CpuBusyMs = sc1 / nc; if (ng > 0) fs.GpuBusyMs = sg / ng; if (nd > 0) fs.DisplayLatencyMs = sd / nd;
            var first = rows.FirstOrDefault();
            if (first != null) { if (rt >= 0 && rt < first.Length) fs.Runtime = first[rt].Trim(); if (pm >= 0 && pm < first.Length) fs.PresentMode = first[pm].Trim(); }
            fs.Frames = fs.Ms.Count; fs.AvgMs = fs.Ms.Average(); fs.AvgFps = 1000.0 / fs.AvgMs;
            var sorted = fs.Ms.OrderByDescending(x => x).ToList();       // worst (longest) frames first
            fs.P1LowFps = 1000.0 / Pct(sorted, 0.01);
            fs.P01LowFps = 1000.0 / Pct(sorted, 0.001);
            var asc = Enumerable.Reverse(sorted).ToList();
            fs.MedianMs = Quantile(asc, 0.5); fs.P99Ms = Quantile(asc, 0.99); fs.P999Ms = Quantile(asc, 0.999); fs.MaxMs = asc[asc.Count - 1];
            fs.Stutters = fs.Ms.Count(x => x > fs.MedianMs * 2.5);
            return fs;
        }
        // average of the worst `frac` of frametimes (standard 1%/0.1% low)
        static double Pct(List<double> sortedDesc, double frac)
        {
            int n = Math.Max(1, (int)(sortedDesc.Count * frac));
            return sortedDesc.Take(n).Average();
        }
        public static double Quantile(List<double> asc, double q)
        {
            if (asc.Count == 0) return 0;
            double pos = q * (asc.Count - 1); int lo = (int)Math.Floor(pos), hi = Math.Min(asc.Count - 1, lo + 1);
            return asc[lo] + (asc[hi] - asc[lo]) * (pos - lo);
        }
    }

    // ---------- A/B test of one tweak (2.7) ----------
    // Alternates OFF/ON captures of the running game (ABAB…) so slow drift (heat, shader cache, background work)
    // hits both sides equally, then compares the means with Welch's t-test. Only tweaks that take effect
    // immediately qualify. The tweak is left as it was before the test.
    public class ABResult
    {
        public List<FrameStats> Off = new List<FrameStats>(), On = new List<FrameStats>();
        public string Error;
        public string Report(string title)
        {
            if (Error != null) return "A/B test stopped: " + Error;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("A/B test: " + title + "   (" + On.Count + " runs each, alternating off/on)");
            Line(sb, "Average fps", Off.Select(f => f.AvgFps), On.Select(f => f.AvgFps));
            Line(sb, "1 % low fps", Off.Select(f => f.P1LowFps), On.Select(f => f.P1LowFps));
            Line(sb, "0.1 % low fps", Off.Select(f => f.P01LowFps), On.Select(f => f.P01LowFps));
            return sb.ToString();
        }
        static void Line(System.Text.StringBuilder sb, string name, IEnumerable<double> a, IEnumerable<double> b)
        {
            var c = ABTest.Compare(a.ToArray(), b.ToArray());
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,-14} off {1,7:0.0}   on {2,7:0.0}   {3,-28}",
                name, c.MeanA, c.MeanB, c.Verdict));
        }
    }

    public struct ABCompare { public double MeanA, MeanB, DiffPct, T; public bool Significant; public string Verdict; }

    public static class ABTest
    {
        // two-sided 95 % critical t by degrees of freedom (rounded down)
        static readonly double[] TCrit = { 0, 12.71, 4.30, 3.18, 2.78, 2.57, 2.45, 2.36, 2.31, 2.26, 2.23 };

        public static ABCompare Compare(double[] a, double[] b)
        {
            var c = new ABCompare { MeanA = a.Length > 0 ? a.Average() : 0, MeanB = b.Length > 0 ? b.Average() : 0 };
            c.DiffPct = c.MeanA > 0 ? (c.MeanB - c.MeanA) / c.MeanA * 100 : 0;
            if (a.Length < 2 || b.Length < 2) { c.Verdict = "need 2+ runs each"; return c; }
            double va = Var(a), vb = Var(b), se2 = va / a.Length + vb / b.Length;
            if (se2 <= 0) { c.Significant = c.MeanA != c.MeanB; c.T = double.PositiveInfinity; }
            else
            {
                c.T = (c.MeanB - c.MeanA) / Math.Sqrt(se2);
                // Welch–Satterthwaite degrees of freedom
                double df = se2 * se2 / (Math.Pow(va / a.Length, 2) / (a.Length - 1) + Math.Pow(vb / b.Length, 2) / (b.Length - 1));
                int d = (int)Math.Floor(df); double crit = d < 1 ? TCrit[1] : d >= TCrit.Length ? 2.0 : TCrit[d];
                c.Significant = Math.Abs(c.T) >= crit;
            }
            string pct = (c.DiffPct >= 0 ? "+" : "") + c.DiffPct.ToString("0.0", CultureInfo.InvariantCulture) + " %";
            c.Verdict = !c.Significant ? pct + "  within noise" : (c.DiffPct > 0 ? pct + "  better" : pct + "  worse");
            return c;
        }
        static double Var(double[] x) { double m = x.Average(); return x.Sum(v => (v - m) * (v - m)) / (x.Length - 1); }

        public static string Unsupported(Tweak t)
        {
            if (!t.Applicable) return "This tweak doesn't apply to this PC.";
            if (t.Reboot) return "This tweak needs a restart, so you can't switch it during a game.";
            if (t.Relogon) return "This tweak needs a sign-out, so you can't switch it during a game.";
            return null;
        }

        // Blocks for about rounds × 2 × (seconds + 8) seconds. progress gets one line per step.
        public static ABResult Run(Tweak t, string process, int seconds, int rounds, Action<string> progress)
        {
            var r = new ABResult();
            string why = Unsupported(t); if (why != null) { r.Error = why; return r; }
            bool wasOn = StateCache.Applied(t);
            try
            {
                for (int i = 1; i <= rounds; i++)
                {
                    foreach (bool on in new[] { false, true })
                    {
                        progress(string.Format("Run {0}/{1}: tweak {2}, settling 5 s…", i, rounds, on ? "ON" : "OFF"));
                        PowerCache.Batch(() => { if (on) t.Apply(); else t.Revert(); });
                        Thread.Sleep(5000);
                        progress(string.Format("Run {0}/{1}: capturing {2} s with the tweak {3}…", i, rounds, seconds, on ? "ON" : "OFF"));
                        string err; var fs = PresentMon.Capture(process, seconds, out err);
                        if (fs == null) { r.Error = err ?? "capture failed"; return r; }
                        (on ? r.On : r.Off).Add(fs);
                    }
                }
                return r;
            }
            finally
            {
                try { PowerCache.Batch(() => { if (wasOn) t.Apply(); else t.Revert(); }); } catch (Exception ex) { Log.W("A/B: restoring {0} failed: {1}", t.Title, ex.Message); }
                progress("Tweak put back to " + (wasOn ? "ON" : "OFF") + " (as before the test).");
            }
        }
    }
}
