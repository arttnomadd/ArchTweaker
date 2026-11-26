// 3.43: Stutter finder page. Pick the running game, click Find stutters and go back to the game. Afterwards each slow
// frame is listed with what happened at that moment (a driver delay, a program starting, a busy disk or CPU, or
// nothing outside the game), plus what to do about the main cause. The engine is StutterFinder.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ArchTweaker
{
    public class StutterPage : Panel, IPage
    {
        readonly ComboBox game = Theme.Drop(240, "Game", 8);
        readonly ComboBox secs = Theme.Drop(70, "Recording length");
        readonly ListView lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
        readonly Label status, summary, advice;
        readonly FlowLayoutPanel actions;
        readonly StutterTimeline chart = new StutterTimeline { Dock = DockStyle.Top, Height = 170, Visible = false };
        readonly Button runBtn, copyBtn;
        StutterFinder.Report last;
        bool running;
        const string NoGame = "Choose the running game";

        public StutterPage()
        {
            BackColor = Theme.Bg;
            Theme.DarkList(lv);
            lv.Columns.Add("Time", 70); lv.Columns.Add("Frame", 80); lv.Columns.Add("Cause", 120); lv.Columns.Add("What happened", 640);
            lv.ShowItemToolTips = true;

            var bar = Theme.Bar();
            bar.Controls.Add(Theme.BarLabel("Game:")); bar.Controls.Add(game);
            bar.Controls.Add(Theme.Btn("Refresh", delegate { FillGames(); }));
            bar.Controls.Add(Theme.BarLabel("Record:")); secs.Items.AddRange(new object[] { "30 s", "60 s", "120 s" }); secs.SelectedIndex = 1; bar.Controls.Add(secs);
            runBtn = Theme.Lead("Find stutters", delegate { Run(); });
            bar.Controls.Add(runBtn);
            copyBtn = Theme.Btn("Copy report", delegate { Copy(); }); copyBtn.Enabled = false;
            bar.Controls.Add(copyBtn);
            status = Theme.StatusText();
            bar.Controls.Add(status);

            summary = Theme.GrowToFit(new Label { Dock = DockStyle.Top, Height = 26, Font = Theme.FB, ForeColor = Theme.Text, UseMnemonic = false, Padding = new Padding(2, 6, 2, 0) });
            advice = Theme.GrowToFit(new Label { Dock = DockStyle.Top, Height = 22, Font = Theme.F, ForeColor = Theme.Muted, UseMnemonic = false, Padding = new Padding(2, 4, 2, 0) });
            actions = Theme.Bar(); actions.Visible = false;

            chart.Picked += s => { foreach (ListViewItem it in lv.Items) if (it.Tag == s) { it.Selected = true; it.Focused = true; it.EnsureVisible(); lv.Focus(); } };
            lv.SelectedIndexChanged += delegate { chart.Selected = lv.SelectedItems.Count == 0 ? null : lv.SelectedItems[0].Tag as StutterFinder.Stutter; };
            Controls.Add(lv); Controls.Add(chart); Controls.Add(actions); Controls.Add(advice); Controls.Add(summary);
            Controls.Add(Theme.Note("Start the game, pick it here, click Find stutters and go back to the game. ArchTweaker records every frame, every long driver delay (DPC/ISR), every program that starts, and the CPU and disk load, all on one clock. Then it shows each slow frame with what happened at that moment. Needs PresentMon (see the Benchmark page)."));
            Controls.Add(bar);
            Skin.SetEmpty(lv, "No recording yet. Start the game, then click Find stutters.");
        }

        public void OnShow() { FillGames(); }
        public void Filter(string q) { }

        void FillGames()
        {
            string keep = game.SelectedItem as string;
            if (keep == null) try { keep = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "BenchGame") as string; } catch { }   // the Benchmark page's game
            game.Items.Clear(); game.Items.Add(L.T(NoGame));
            foreach (var g in Bench.Games()) game.Items.Add(g);
            int i = keep == null ? -1 : game.Items.IndexOf(keep);
            game.SelectedIndex = i >= 0 ? i : game.Items.Count > 1 ? 1 : 0;
        }

        void Run()
        {
            if (running) return;
            string g = game.SelectedIndex > 0 ? game.SelectedItem as string : null;
            if (g == null) { Dlg.Error("Choose the game", "Start the game first, click Refresh, then pick it in the list."); return; }
            if (!PresentMon.Available) { Dlg.Error("PresentMon.exe not found", "Open the Benchmark page and click Choose PresentMon.exe… to add it."); return; }
            int s = secs.SelectedIndex == 0 ? 30 : secs.SelectedIndex == 2 ? 120 : 60;
            running = true; runBtn.Enabled = false;
            StutterFinder.Report res = null;
            Action<string> say = Theme.SayTo(this, status);
            AppShell.Current.Busy(L.F("Recording {0} for {1} s…", g, s), delegate { res = StutterFinder.Run(g, s, say); }, delegate
            {
                running = false; runBtn.Enabled = true; status.Text = "";
                ShowReport(res);
            });
        }

        void ShowReport(StutterFinder.Report r)
        {
            last = r;
            summary.Text = r == null ? L.T("The recording stopped. The log has the details.") : StutterFinder.Summary(r);
            summary.ForeColor = r == null || r.Error != null ? Theme.Warn : Theme.Text;
            string a = r == null ? null : StutterFinder.Advice(r);
            if (r != null && r.TraceError != null) a = (a == null ? "" : a + " ") + L.T("The driver trace didn't run:") + " " + L.T(r.TraceError);
            advice.Text = a ?? ""; advice.Visible = a != null;
            ShowActions(r);
            chart.Set(r); chart.Visible = r != null && r.Error == null && r.Frames >= 10;
            lv.BeginUpdate(); lv.Items.Clear();
            if (r != null)
                foreach (var s in r.Stutters)
                {
                    var it = new ListViewItem(new[] { Clock(s.AtSec), s.Ms.ToString("0.0", CultureInfo.CurrentCulture) + " ms", CauseName(s.Kind), s.Text }) { Tag = s, ToolTipText = s.Text };
                    it.ForeColor = s.Kind == StutterFinder.Cause.Game ? Theme.Muted : Theme.Text;
                    lv.Items.Add(it);
                }
            lv.EndUpdate();
            Skin.Loaded(lv, r != null && r.Error == null && r.Frames >= 10 ? "No slow frames in this recording." : "No recording yet. Start the game, then click Find stutters.");
            copyBtn.Enabled = r != null && r.Error == null && r.Frames >= 10;
        }

        // Buttons for the pages that deal with the main cause
        void ShowActions(StutterFinder.Report r)
        {
            actions.Controls.Clear();
            var pages = new List<string>();
            if (r != null && r.Stutters.Count > 0)
                switch (r.Main)
                {
                    case StutterFinder.Cause.Driver: pages.AddRange(new[] { "Latency", "Interrupt tuner" }); break;
                    case StutterFinder.Cause.Program: pages.AddRange(new[] { "Startup", "Scheduled tasks" }); break;
                    case StutterFinder.Cause.Disk: pages.AddRange(new[] { "Services", "Disk space" }); break;
                    case StutterFinder.Cause.Cpu: pages.Add("Startup"); break;
                }
            foreach (var p in pages)
            {
                string page = p;
                actions.Controls.Add(Theme.Btn(L.F("Open {0}", L.T(page)), delegate { AppShell.Current.ShowPage(page); }));
            }
            actions.Visible = actions.Controls.Count > 0;
        }

        static string Clock(double sec) { int s = (int)sec; return (s / 60) + ":" + (s % 60).ToString("00") + "." + ((int)((sec - s) * 10)).ToString(CultureInfo.InvariantCulture); }

        public static Color CauseColor(StutterFinder.Cause c)
        {
            switch (c)
            {
                case StutterFinder.Cause.Driver: return Theme.Risk;
                case StutterFinder.Cause.Program: return Theme.Warn;
                case StutterFinder.Cause.Disk: return Theme.Exp;
                case StutterFinder.Cause.Cpu: return Theme.Accent;
                default: return Theme.Muted;
            }
        }

        public static string CauseName(StutterFinder.Cause c)
        {
            switch (c)
            {
                case StutterFinder.Cause.Driver: return L.T("Driver delay");
                case StutterFinder.Cause.Program: return L.T("Program started");
                case StutterFinder.Cause.Disk: return L.T("Disk busy");
                case StutterFinder.Cause.Cpu: return L.T("CPU busy");
                default: return L.T("The game");
            }
        }

        void Copy()
        {
            var r = last; if (r == null) return;
            var sb = new StringBuilder();
            sb.AppendLine("ArchTweaker " + Program.Version + " · " + L.T("Stutter finder") + " · " + r.Game + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine(StutterFinder.Summary(r));
            string a = StutterFinder.Advice(r); if (a != null) sb.AppendLine(a);
            sb.AppendLine();
            foreach (var s in r.Stutters) sb.AppendLine(Clock(s.AtSec) + "\t" + s.Ms.ToString("0.0", CultureInfo.InvariantCulture) + " ms\t" + CauseName(s.Kind) + "\t" + s.Text);
            try { Clipboard.SetText(sb.ToString()); AppShell.Current.Notify("Report copied.", Toast.Kind.Ok); }
            catch (Exception ex) { Log.W("Stutter finder: copy: {0}", ex.Message); AppShell.Current.Notify("Couldn't copy the report.", Toast.Kind.Warn); }
        }
    }

    // 3.44: the whole recording as a frame-time line, the "slow" line, and a coloured dot on each slow frame by cause.
    // Point at a dot to read what happened; click it to select its row in the list. Painted in proportion to its size.
    public class StutterTimeline : Control
    {
        StutterFinder.Report r;
        StutterFinder.Stutter hot, sel;
        float grow = 1f;   // 3.45: 0 → 1 over 100 ms (ease-out) while the pointed-at dot grows from 4.5 to 6 px
        readonly ToolTip tip = Tips.New(new ToolTip { InitialDelay = 100, AutoPopDelay = 15000 });
        public event Action<StutterFinder.Stutter> Picked;
        public StutterFinder.Stutter Selected { get { return sel; } set { sel = value; Invalidate(); } }

        public StutterTimeline()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg; AccessibleRole = AccessibleRole.Chart; AccessibleName = L.T("Frame times with slow frames marked by cause");
            Disposed += delegate { tip.Dispose(); };
        }

        public void Set(StutterFinder.Report report) { r = report; hot = sel = null; Invalidate(); }

        float U { get { return Height / 170f; } }
        RectangleF Plot { get { float u = U; return new RectangleF(52 * u, 10 * u, Width - 64 * u, Height - 38 * u); } }
        double Span { get { return r == null || r.AtSec.Length == 0 ? 1 : Math.Max(1e-6, r.AtSec.Max()); } }
        double Ceil { get { return r == null || r.FrameMs.Length == 0 ? 1 : Math.Max(r.ThresholdMs * 1.6, Math.Min(r.FrameMs.Max(), r.ThresholdMs * 4)); } }
        PointF At(double sec, double ms)
        {
            var p = Plot;
            return new PointF(p.Left + (float)(sec / Span * p.Width), p.Bottom - (float)(Math.Min(ms, Ceil) / Ceil * p.Height));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            if (r == null || r.FrameMs.Length < 2) return;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float u = U; var p = Plot;
            Gfx.Surface(g, new RectangleF(p.Left - 8 * u, p.Top - 6 * u, p.Width + 16 * u, p.Height + 12 * u), Theme.Panel, false);
            // the "slow" line and the median, labelled on the left
            float ty = At(0, r.ThresholdMs).Y, my = At(0, r.MedianMs).Y;
            using (var pen = new Pen(Theme.Warn, 1f * u)) { pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash; g.DrawLine(pen, p.Left, ty, p.Right, ty); }
            var lf = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, r.ThresholdMs.ToString("0") + " ms", Theme.FS, new Rectangle(0, (int)(ty - 8 * u), (int)(p.Left - 12 * u), (int)(16 * u)), Theme.Warn, lf);
            if (Math.Abs(my - ty) > 14 * u) TextRenderer.DrawText(g, r.MedianMs.ToString("0.0") + " ms", Theme.FS, new Rectangle(0, (int)(my - 8 * u), (int)(p.Left - 12 * u), (int)(16 * u)), Theme.Muted, lf);
            // frame times: the highest frame per pixel column, so a one-frame spike is never lost
            int cols = Math.Max(2, (int)p.Width);
            var top = new double[cols];
            for (int i = 0; i < r.FrameMs.Length; i++) { int c = Math.Min(cols - 1, (int)(r.AtSec[i] / Span * (cols - 1))); if (r.FrameMs[i] > top[c]) top[c] = r.FrameMs[i]; }
            var pts = new PointF[cols]; double last = r.MedianMs;
            for (int c = 0; c < cols; c++) { double v = top[c] > 0 ? top[c] : last; last = v; pts[c] = new PointF(p.Left + c * p.Width / (cols - 1), p.Bottom - (float)(Math.Min(v, Ceil) / Ceil * p.Height)); }
            using (var pen = new Pen(Theme.Text, 1.2f * u)) g.DrawLines(pen, pts);
            // slow frames, coloured by cause
            foreach (var s in r.Stutters)
            {
                var pt = At(s.AtSec, s.Ms); float rad = (s == sel ? 6f : s == hot ? 4.5f + 1.5f * grow : 4.5f) * u;
                using (var b = new SolidBrush(StutterPage.CauseColor(s.Kind))) g.FillEllipse(b, pt.X - rad, pt.Y - rad, rad * 2, rad * 2);
                if (s == sel) using (var pen = new Pen(Theme.Strong, 1.5f * u)) g.DrawEllipse(pen, pt.X - rad - 2 * u, pt.Y - rad - 2 * u, (rad + 2 * u) * 2, (rad + 2 * u) * 2);
            }
            // time axis and the legend with counts
            int y = (int)(p.Bottom + 8 * u), h = (int)(18 * u);
            TextRenderer.DrawText(g, "0:00", Theme.FS, new Rectangle((int)p.Left, y, (int)(60 * u), h), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            int secs = (int)Math.Round(Span);
            TextRenderer.DrawText(g, (secs / 60) + ":" + (secs % 60).ToString("00"), Theme.FS, new Rectangle((int)(p.Right - 60 * u), y, (int)(60 * u), h), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.NoPadding);
            float x = p.Left + 56 * u;
            foreach (StutterFinder.Cause c in Enum.GetValues(typeof(StutterFinder.Cause)))
            {
                int n = r.Count(c); if (n == 0) continue;
                using (var b = new SolidBrush(StutterPage.CauseColor(c))) g.FillEllipse(b, x, y + 4 * u, 8 * u, 8 * u);
                string t = StutterPage.CauseName(c) + " " + n;
                int tw = TextRenderer.MeasureText(t, Theme.FS).Width;
                TextRenderer.DrawText(g, t, Theme.FS, new Point((int)(x + 12 * u), y), Theme.Muted);
                x += 12 * u + tw + 14 * u;
            }
        }

        StutterFinder.Stutter Near(Point m)
        {
            if (r == null) return null;
            StutterFinder.Stutter best = null; double bd = 12 * U;
            foreach (var s in r.Stutters) { var pt = At(s.AtSec, s.Ms); double d = Math.Sqrt((pt.X - m.X) * (pt.X - m.X) + (pt.Y - m.Y) * (pt.Y - m.Y)); if (d < bd) { bd = d; best = s; } }
            return best;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            var n = Near(e.Location);
            if (n != hot)
            {
                hot = n; Invalidate(); Cursor = n != null ? Cursors.Hand : Cursors.Default;
                if (n != null && Motion.On) { grow = 0f; double t = 0; Motion.Run(this, dt => { t = Math.Min(1, t + dt / 100.0); grow = (float)(1 - Math.Pow(1 - t, 3)); Invalidate(); return t >= 1; }); }
                else grow = 1f;
                if (n == null) tip.Hide(this);
                else tip.Show(StutterPage.CauseName(n.Kind) + " · " + n.Ms.ToString("0.0") + " ms" + Environment.NewLine + n.Text, this, e.X + 14, e.Y + 14);
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hot = null; tip.Hide(this); Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            var n = Near(e.Location);
            if (n != null) { sel = n; Invalidate(); var h = Picked; if (h != null) h(n); }
            base.OnMouseClick(e);
        }
    }
}
