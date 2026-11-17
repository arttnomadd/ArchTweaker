// ArchTweaker: A/B tests that continue after a restart.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ---------------- restart A/B test ----------------
    // For tweaks that need a restart: restart → measure (A) → apply → restart → measure (B) → verdict.
    // Tweaks that take effect at once: measure, apply, measure, in one go. The PC sits idle while it measures.
    public static class RebootAB
    {
        const string K = @"Software\ArchTweaker\RebootAB";
        public const int Runs = 3, RunSeconds = 15, SettleSeconds = 90;
        public static readonly string[] Metrics = { "DPC time %", "Interrupt time %", "Sleep(1) average ms", "Sleep(1) p99 ms" };

        public static string PendingId { get { return Reg.Get(Hive.HKCU, K, "Id") as string; } }
        public static string Phase { get { return Reg.Get(Hive.HKCU, K, "Phase") as string; } }

        public static bool Testable(Tweak t) { return t.Applicable && !t.Dangerous && (t.Ops.Count > 0 || t.ApplyCmds.Count > 0) && t.UndoNote == null; }

        // [metric][run]; lower is better for every metric
        public static double[][] Measure(Action<string> progress)
        {
            var m = Enumerable.Range(0, Metrics.Length).Select(_ => new double[Runs]).ToArray();
            for (int r = 0; r < Runs; r++)
            {
                if (progress != null) progress(string.Format(CultureInfo.InvariantCulture, "run {0}/{1}", r + 1, Runs));
                double d, i; CoreCounters.Average(RunSeconds * 1000, out d, out i);
                double avg, p99; SleepStats(1000, out avg, out p99);
                m[0][r] = d; m[1][r] = i; m[2][r] = avg; m[3][r] = p99;
            }
            return m;
        }
        public static void SleepStats(int samples, out double avg, out double p99)
        {
            var d = new List<double>(samples); var sw = new Stopwatch();
            Thread.Sleep(1);
            for (int i = 0; i < samples; i++) { sw.Restart(); Thread.Sleep(1); sw.Stop(); d.Add(sw.Elapsed.TotalMilliseconds); }
            d.Sort(); avg = d.Average(); p99 = d[Math.Max(0, (int)(d.Count * 0.99) - 1)];
        }

        public static string Ser(double[][] m) { return string.Join(";", m.Select(r => string.Join(",", r.Select(x => x.ToString("R", CultureInfo.InvariantCulture)).ToArray())).ToArray()); }
        public static double[][] De(string s)
        {
            return (s ?? "").Split(';').Select(r => r.Split(',').Where(x => x.Length > 0).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray()).ToArray();
        }

        static void Save(string id, string phase, double[][] a)
        {
            using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(K))
            { k.SetValue("Id", id); k.SetValue("Phase", phase); if (a != null) k.SetValue("A", Ser(a)); }
        }
        public static void Clear() { using (var b = Reg.Base(Hive.HKCU)) b.DeleteSubKeyTree(K, false); AutoResume.Disarm(); }

        // restart tweaks: remember the test, restart for a clean baseline
        public static void Begin(Tweak t) { Save(t.Id, "before", null); AutoResume.Arm(); }   // 3.8: ArchTweaker opens by itself after the restart

        // a verdict for each metric; worse = a significant rise in any metric
        public static string Verdict(double[][] a, double[][] b, out bool worse, out bool better)
        {
            worse = better = false;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Metrics.Length && i < a.Length && i < b.Length; i++)
            {
                if (a[i].Any(x => x < 0) || b[i].Any(x => x < 0)) { sb.AppendLine(L.T(Metrics[i]) + ": " + L.T("not measured on this PC")); continue; }
                var c = ABTest.Compare(a[i], b[i]);
                string dir = !c.Significant ? L.T("no real change") : c.MeanB < c.MeanA ? L.T("better") : L.T("worse");
                if (c.Significant && c.MeanB > c.MeanA) worse = true;
                if (c.Significant && c.MeanB < c.MeanA) better = true;
                sb.AppendLine(string.Format(CultureInfo.CurrentCulture, "{0}: {1:0.###} → {2:0.###} ({3:+0.0;-0.0;0} %) · {4}", L.T(Metrics[i]), c.MeanA, c.MeanB, c.DiffPct, dir));
            }
            return sb.ToString();
        }

        // called on start-up; runs the next step if a test is pending (on a background thread)
        public static void Resume(Action<string> say, Action<Tweak, string, bool> result, Action askRestart)
        {
            string id = PendingId; if (id == null) return;
            var t = Catalog.All.FirstOrDefault(x => x.Id == id);
            if (t == null) { Clear(); return; }
            string phase = Phase;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    say(L.F("Restart A/B test for '{0}': measuring in {1} s. Leave the PC idle.", L.T(t.Title), SettleSeconds));
                    Thread.Sleep(SettleSeconds * 1000);
                    var m = Measure(p => say(L.F("Restart A/B test: measuring ({0}).", p)));
                    if (phase == "before")
                    {
                        t.Apply();
                        Save(t.Id, "after", m);
                        say(L.F("Restart A/B test: baseline done, '{0}' applied. Restart to measure it.", L.T(t.Title)));
                        AutoResume.Arm();   // 3.8
                        askRestart();
                    }
                    else
                    {
                        var a = De(Reg.Get(Hive.HKCU, K, "A") as string);
                        bool worse, better; string v = Verdict(a, m, out worse, out better);
                        Clear();
                        result(t, v, worse);
                    }
                }
                catch (Exception ex) { Log.W("Restart A/B test: {0}", ex.Message); Clear(); }
            });
        }
    }
}
