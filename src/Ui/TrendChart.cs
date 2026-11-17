// ArchTweaker: Trend chart for saved runs.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ================= TREND CHART =================
    // One line over the saved runs (oldest left). Lower is better. Hover shows the run. Laid out in S() units.
    public class TrendChart : Control
    {
        public class Point2 { public DateTime When; public double V; }
        List<Point2> pts = new List<Point2>(); string title = "", unit = "", fmt = "0"; int hot = -1;
        public TrendChart()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg; Dock = DockStyle.Top; Height = Theme.S(150); Visible = false;
            AccessibleRole = AccessibleRole.Chart;
        }
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified) { }
        public bool Higher;   // 3.18: FPS charts: a bigger value is better
        public void Set(string chartTitle, string chartUnit, string format, List<Point2> points)
        {
            title = chartTitle; unit = chartUnit; fmt = format; pts = points ?? new List<Point2>();
            Visible = pts.Count >= 2;
            AccessibleName = L.T(title) + ": " + string.Join(", ", pts.Select(p => p.When.ToString("dd MMM") + " " + p.V.ToString(fmt) + " " + unit).ToArray());
            Invalidate();
        }
        Rectangle Plot { get { int l = Theme.S(52), t = Theme.S(28), r = Theme.S(16), b = Theme.S(24); return new Rectangle(l, t, Math.Min(Width, Theme.S(Theme.MaxContent)) - l - r, Height - t - b); } }
        PointF At(int i, double lo, double hi)
        {
            var p = Plot; float x = pts.Count == 1 ? p.X : p.X + p.Width * i / (float)(pts.Count - 1);
            float y = (float)(p.Bottom - (hi <= lo ? 0.5 : (pts[i].V - lo) / (hi - lo)) * p.Height);
            return new PointF(x, y);
        }
        void Range(out double lo, out double hi)
        {
            lo = 0; hi = pts.Max(p => p.V);
            if (hi <= 0) hi = 1;
            double step = Nice(hi / 3); hi = Math.Ceiling(hi / step) * step;
        }
        public static double Nice(double v) { double e = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(v, 1e-9)))), f = v / e; return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * e; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            if (pts.Count < 2) return;
            TextRenderer.DrawText(g, L.T(title) + "  (" + unit + ", " + L.T(Higher ? "higher is better" : "lower is better") + ")", Theme.FS, new Point(0, Theme.S(4)), Theme.Muted, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            var p = Plot; double lo, hi; Range(out lo, out hi);
            using (var grid = new Pen(Theme.Grid))
                for (int k = 0; k <= 3; k++)
                {
                    float y = p.Bottom - p.Height * k / 3f; double v = lo + (hi - lo) * k / 3;
                    g.DrawLine(grid, p.X, y, p.Right, y);
                    TextRenderer.DrawText(g, v.ToString(hi < 10 ? "0.#" : "0"), Theme.FT, new Rectangle(0, (int)y - Theme.S(8), p.X - Theme.S(8), Theme.S(16)), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            TextRenderer.DrawText(g, pts[0].When.ToString("dd MMM"), Theme.FT, new Point(p.X, p.Bottom + Theme.S(6)), Theme.Muted, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            string last = pts[pts.Count - 1].When.ToString("dd MMM");
            int lw = TextRenderer.MeasureText(last, Theme.FT, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(g, last, Theme.FT, new Point(p.Right - lw, p.Bottom + Theme.S(6)), Theme.Muted, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            var line = Enumerable.Range(0, pts.Count).Select(i => At(i, lo, hi)).ToArray();
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Accent, Math.Max(2f, 2f * Theme.Scale)) { LineJoin = LineJoin.Round }) g.DrawLines(pen, line);
            float r = Theme.S(4);
            for (int i = 0; i < line.Length; i++)
                using (var b = new SolidBrush(i == hot ? Theme.Strong : Theme.Accent)) using (var ring = new Pen(BackColor, Math.Max(2f, 2f * Theme.Scale)))
                { g.FillEllipse(b, line[i].X - r, line[i].Y - r, r * 2, r * 2); g.DrawEllipse(ring, line[i].X - r, line[i].Y - r, r * 2, r * 2); }
            g.SmoothingMode = old;
            int show = hot >= 0 ? hot : pts.Count - 1;
            string lab = pts[show].V.ToString(fmt) + " " + unit + (hot >= 0 ? "  ·  " + pts[show].When.ToString("dd MMM HH:mm") : "");
            var ls = TextRenderer.MeasureText(lab, Theme.FT, Size.Empty, TextFormatFlags.NoPadding);
            float lx = Math.Min(Math.Max(line[show].X - ls.Width / 2f, p.X), p.Right - ls.Width), ly = line[show].Y - Theme.S(20);
            if (ly < p.Y - Theme.S(4)) ly = line[show].Y + Theme.S(8);
            var lr = new Rectangle((int)lx - Theme.S(6), (int)ly - Theme.S(2), ls.Width + Theme.S(12), ls.Height + Theme.S(4));
            using (var b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, lr);
            using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, lr.X, lr.Y, lr.Width - 1, lr.Height - 1);
            TextRenderer.DrawText(g, lab, Theme.FT, lr, Theme.Strong, Theme.Panel, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (pts.Count < 2) return;
            double lo, hi; Range(out lo, out hi); int best = -1; float bd = Theme.S(24);
            for (int i = 0; i < pts.Count; i++) { float d = Math.Abs(At(i, lo, hi).X - e.X); if (d < bd) { bd = d; best = i; } }
            if (best != hot) { hot = best; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }
    }
}
