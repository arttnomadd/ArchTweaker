// ArchTweaker: Saved Measure runs with a baseline that survives a restart.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ================= MEASURE RUNS =================
    public class MeasureRun { public bool Baseline; public MetricSet M; }

    public static class MeasureRuns
    {
        const int Keep = 20;
        static string File_ { get { return Path.Combine(Paths.Root, "measure-runs.tsv"); } }
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Save(MetricSet m, bool baseline)
        {
            if (m == null) return;
            try
            {
                var lines = File.Exists(File_) ? File.ReadAllLines(File_).ToList() : new List<string>();
                lines.Add(string.Join("\t", new[] {
                    m.When.ToString("yyyy-MM-dd HH:mm:ss", Inv), baseline ? "B" : "A", m.TimerMs.ToString("0.0000", Inv), m.Sleep1Avg.ToString("0.000", Inv),
                    m.Sleep1P99.ToString("0.000", Inv), m.Sleep1Max.ToString("0.000", Inv), m.JitterMaxUs.ToString("0.0", Inv), m.DpcPct.ToString("0.000", Inv), m.IsrPct.ToString("0.000", Inv) }));
                // the newest baseline is never dropped: "after" runs are compared with it
                while (lines.Count > Keep)
                {
                    int lastB = lines.FindLastIndex(l => l.Split('\t').Length > 1 && l.Split('\t')[1] == "B");
                    lines.RemoveAt(lastB == 0 ? 1 : 0);
                }
                File.WriteAllLines(File_, lines);
            }
            catch (Exception ex) { Log.W("Measure runs: {0}", ex.Message); }
        }

        // oldest first; a damaged line is skipped
        public static List<MeasureRun> All()
        {
            var o = new List<MeasureRun>();
            try
            {
                if (!File.Exists(File_)) return o;
                foreach (var l in File.ReadAllLines(File_))
                {
                    var p = l.Split('\t'); if (p.Length < 9) continue;
                    var m = new MetricSet();
                    if (!DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm:ss", Inv, DateTimeStyles.None, out m.When)) continue;
                    double.TryParse(p[2], NumberStyles.Float, Inv, out m.TimerMs); double.TryParse(p[3], NumberStyles.Float, Inv, out m.Sleep1Avg);
                    double.TryParse(p[4], NumberStyles.Float, Inv, out m.Sleep1P99); double.TryParse(p[5], NumberStyles.Float, Inv, out m.Sleep1Max);
                    double.TryParse(p[6], NumberStyles.Float, Inv, out m.JitterMaxUs); double.TryParse(p[7], NumberStyles.Float, Inv, out m.DpcPct); double.TryParse(p[8], NumberStyles.Float, Inv, out m.IsrPct);
                    o.Add(new MeasureRun { Baseline = p[1] == "B", M = m });
                }
            }
            catch (Exception ex) { Log.W("Measure runs: {0}", ex.Message); }
            return o;
        }
        public static MetricSet Baseline { get { var b = All().LastOrDefault(r => r.Baseline); return b == null ? null : b.M; } }
        public static void Clear() { try { if (File.Exists(File_)) File.Delete(File_); } catch { } }
    }

    // Newest Measure run against the saved baseline.
    public class MeasureCompare : CompareCards
    {
        public readonly TrendChart Trend = new TrendChart();   // 3.17: every saved run, oldest left
        public void Reload()
        {
            var all = MeasureRuns.All();
            Trend.Set("Scheduling jitter (max) per run", "µs", "0.0", all.Select(r => new TrendChart.Point2 { When = r.M.When, V = r.M.JitterMaxUs }).ToList());
            var last = all.LastOrDefault();
            if (last == null) { Set("", false, null); return; }
            var bl = all.LastOrDefault(r => r.Baseline);
            MetricSet b = last.M, a = bl != null && !last.Baseline ? bl.M : null;
            string when = b.When.ToString("dd MMM HH:mm");
            string h = last.Baseline ? L.T("Baseline:") + " " + when + ". " + L.T("Apply tweaks, restart if asked, then click Measure again (after).")
                : a == null ? L.T("Newest run:") + " " + when + ". " + L.T("ArchTweaker has no baseline to compare with.")
                : L.T("Newest run:") + " " + when + "   ·   " + L.T("baseline:") + " " + a.When.ToString("dd MMM HH:mm");
            Func<Func<MetricSet, double>, double?> was = f => a == null ? (double?)null : f(a);
            Set(h, false, new List<Card>
            {
                new Card { Name = "Timer resolution", Unit = "ms", Fmt = "0.000", Now = b.TimerMs, Was = was(m => m.TimerMs) },
                new Card { Name = "Sleep(1) average", Unit = "ms", Fmt = "0.00", Now = b.Sleep1Avg, Was = was(m => m.Sleep1Avg) },
                new Card { Name = "Sleep(1) 99th percentile", Unit = "ms", Fmt = "0.00", Now = b.Sleep1P99, Was = was(m => m.Sleep1P99) },
                new Card { Name = "Scheduling jitter (max)", Unit = "µs", Fmt = "0.0", Now = b.JitterMaxUs, Was = was(m => m.JitterMaxUs) },
            });
        }
    }
}
