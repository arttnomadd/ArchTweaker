// ArchTweaker: Saved latency runs and their before/after comparison.
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
    // ================= LATENCY RUNS =================
    public class LatencyRun
    {
        public DateTime When; public int Secs, Over500, Over1000, Applied; public double DpcUs, IsrUs, BusyMs; public string Worst = "", Plan = "";
        public double WorstUs { get { return Math.Max(DpcUs, IsrUs); } }
        // per minute / per second, so a 10 s and a 60 s scan can be compared
        public double SpikesPerMin { get { return Secs > 0 ? Over500 * 60.0 / Secs : 0; } }
        public double BusyMsPerSec { get { return Secs > 0 ? BusyMs / Secs : 0; } }
    }

    public static class LatencyRuns
    {
        const int Keep = 20;
        static string File_ { get { return Path.Combine(Paths.Root, "latency-runs.tsv"); } }
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static LatencyRun From(List<DriverStat> res, int secs)
        {
            var r = new LatencyRun { When = DateTime.Now, Secs = secs };
            if (res != null && res.Count > 0)
            {
                r.DpcUs = res.Max(x => x.DpcMaxUs); r.IsrUs = res.Max(x => x.IsrMaxUs);
                r.Over500 = res.Sum(x => x.Over500); r.Over1000 = res.Sum(x => x.Over1000);
                r.BusyMs = res.Sum(x => x.DpcTotalUs + x.IsrTotalUs) / 1000.0;
                var w = res.OrderByDescending(x => x.MaxUs).First(); r.Worst = w.Driver ?? "";
            }
            try { var a = PowerExplorer.Schemes().FirstOrDefault(s => s.Active); if (a != null) r.Plan = a.Name ?? ""; } catch { }
            try { r.Applied = AppliedSet.Read().Count; } catch { }
            return r;
        }

        public static void Save(LatencyRun r)
        {
            try
            {
                var lines = File.Exists(File_) ? File.ReadAllLines(File_).ToList() : new List<string>();
                lines.Add(string.Join("\t", new[] {
                    r.When.ToString("yyyy-MM-dd HH:mm:ss", Inv), r.Secs.ToString(Inv), r.DpcUs.ToString("0.0", Inv), r.IsrUs.ToString("0.0", Inv),
                    r.Over500.ToString(Inv), r.Over1000.ToString(Inv), r.BusyMs.ToString("0.0", Inv), Clean(r.Worst), Clean(r.Plan), r.Applied.ToString(Inv) }));
                if (lines.Count > Keep) lines = lines.Skip(lines.Count - Keep).ToList();
                File.WriteAllLines(File_, lines);
            }
            catch (Exception ex) { Log.W("Latency runs: {0}", ex.Message); }
        }
        static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

        // oldest first; a damaged line is skipped
        public static List<LatencyRun> All()
        {
            var o = new List<LatencyRun>();
            try
            {
                if (!File.Exists(File_)) return o;
                foreach (var l in File.ReadAllLines(File_))
                {
                    var p = l.Split('\t'); if (p.Length < 10) continue;
                    var r = new LatencyRun();
                    if (!DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm:ss", Inv, DateTimeStyles.None, out r.When)) continue;
                    int.TryParse(p[1], NumberStyles.Integer, Inv, out r.Secs);
                    double.TryParse(p[2], NumberStyles.Float, Inv, out r.DpcUs); double.TryParse(p[3], NumberStyles.Float, Inv, out r.IsrUs);
                    int.TryParse(p[4], NumberStyles.Integer, Inv, out r.Over500); int.TryParse(p[5], NumberStyles.Integer, Inv, out r.Over1000);
                    double.TryParse(p[6], NumberStyles.Float, Inv, out r.BusyMs);
                    r.Worst = p[7]; r.Plan = p[8]; int.TryParse(p[9], NumberStyles.Integer, Inv, out r.Applied);
                    o.Add(r);
                }
            }
            catch (Exception ex) { Log.W("Latency runs: {0}", ex.Message); }
            return o;
        }
    }

    // Value cards with the change against an earlier value. Lower is better for every number shown with it.
    // Laid out in S() units, so the page-wide DPI scale pass leaves it alone.
    public class CompareCards : Control
    {
        public struct Card { public string Name, Unit, Fmt; public double Now; public double? Was; public bool Higher; }   // 3.18: Higher = a bigger value is better (FPS)
        List<Card> cards = new List<Card>(); string header = ""; bool warn;
        public CompareCards()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg; Dock = DockStyle.Top; Height = Theme.S(118);
            AccessibleRole = AccessibleRole.Grouping;
        }
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified) { }
        protected override bool ScaleChildren { get { return false; } }
        // change in per cent; lower is better
        public static double Delta(double was, double now) { return was <= 0 ? 0 : (now - was) / was * 100.0; }
        protected void Set(string head, bool warning, List<Card> list)
        {
            header = head ?? ""; warn = warning; cards = list ?? new List<Card>();
            Visible = cards.Count > 0;
            var parts = new List<string>();
            foreach (var m in cards)
            {
                string t = L.T(m.Name) + " " + m.Now.ToString(m.Fmt) + " " + m.Unit;
                if (m.Was.HasValue) { double d = Delta(m.Was.Value, m.Now); t += " (" + (d <= 0 ? "" : "+") + d.ToString("0") + " %)"; }
                parts.Add(t);
            }
            AccessibleName = header + ". " + string.Join(", ", parts.ToArray());
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            if (cards.Count == 0) return;
            int y0 = Theme.S(2);
            TextRenderer.DrawText(g, header, Theme.FS, new Rectangle(0, y0, Width, Theme.S(18)), warn ? Theme.Warn : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            int gap = Theme.S(Theme.Sp.X8), top = y0 + Theme.S(24), h = Height - top - Theme.S(10);
            int w = (Math.Min(Width, Theme.S(Theme.MaxContent)) - gap * (cards.Count - 1)) / cards.Count;
            for (int i = 0; i < cards.Count; i++)
            {
                var m = cards[i]; var r = new Rectangle(i * (w + gap), top, w, h);
                var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = Gfx.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Theme.S(Theme.Radius)))
                { using (var br = new SolidBrush(Theme.Panel)) g.FillPath(br, p); using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, p); }
                g.SmoothingMode = old;
                int px = Theme.S(12);
                TextRenderer.DrawText(g, L.T(m.Name), Theme.FS, new Rectangle(r.X + px, r.Y + Theme.S(8), r.Width - px * 2, Theme.S(16)), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                string val = m.Now.ToString(m.Fmt);
                var vf = Theme.Semi(15f);
                var vs = TextRenderer.MeasureText(g, val, vf, Size.Empty, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, val, vf, new Point(r.X + px, r.Y + Theme.S(26)), Theme.Strong, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, m.Unit, Theme.FS, new Point(r.X + px + vs.Width + Theme.S(4), r.Y + Theme.S(26) + vs.Height - Theme.FS.Height), Theme.Muted, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                if (m.Was.HasValue)
                {
                    double was = m.Was.Value, d = Delta(was, m.Now);
                    bool call = Math.Abs(d) >= 10;   // smaller swings are run-to-run noise
                    bool better = m.Higher ? d > 0 : d < 0;
                    Color c = !call ? Theme.Muted : better ? Theme.Ok : Theme.Bad;
                    string arrow = !call ? "≈" : d < 0 ? "▼" : "▲";
                    string txt = arrow + " " + (was <= 0 ? "—" : Math.Abs(d).ToString("0") + " %") + "   " + L.T("was") + " " + was.ToString(m.Fmt);
                    TextRenderer.DrawText(g, txt, Theme.FT, new Rectangle(r.X + px, r.Bottom - Theme.S(22), r.Width - px * 2, Theme.S(16)), c, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }
        }
    }

    // Newest "Find slow drivers" scan against the one before it.
    public class RunCompare : CompareCards
    {
        public readonly TrendChart Trend = new TrendChart();   // 3.17: every saved scan, oldest left
        public void Reload()
        {
            var all = LatencyRuns.All();
            Trend.Set("Worst DPC/ISR per scan", "µs", "0", all.Select(r => new TrendChart.Point2 { When = r.When, V = r.WorstUs }).ToList());
            var b = all.LastOrDefault(); var a = all.Count > 1 ? all[all.Count - 2] : null;
            if (b == null) { Set("", false, null); return; }
            string nb = b.When.ToString("dd MMM HH:mm") + " (" + b.Secs + " s)";
            bool differ = a != null && (a.Secs != b.Secs || a.Plan != b.Plan);
            string h = a == null ? L.T("Newest scan:") + " " + nb + ". " + L.T("Run another scan after you apply tweaks to see the difference.")
                : L.T("Newest scan:") + " " + nb + "   ·   " + L.T("previous:") + " " + a.When.ToString("dd MMM HH:mm") + " (" + a.Secs + " s)"
                    + (differ ? "   ·   " + L.T("Different length or power plan: compare with care.") : "");
            Func<Func<LatencyRun, double>, double?> was = f => a == null ? (double?)null : f(a);
            Set(h, differ, new List<Card>
            {
                new Card { Name = "Worst DPC", Unit = "µs", Fmt = "0", Now = b.DpcUs, Was = was(r => r.DpcUs) },
                new Card { Name = "Worst ISR", Unit = "µs", Fmt = "0", Now = b.IsrUs, Was = was(r => r.IsrUs) },
                new Card { Name = "Spikes ≥ 500 µs", Unit = "/min", Fmt = "0.0", Now = b.SpikesPerMin, Was = was(r => r.SpikesPerMin) },
                new Card { Name = "Time in DPC/ISR", Unit = "ms/s", Fmt = "0.00", Now = b.BusyMsPerSec, Was = was(r => r.BusyMsPerSec) },
            });
        }
    }
}
