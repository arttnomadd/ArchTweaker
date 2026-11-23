// ArchTweaker: Home dashboard: latency chain, vitals and status tiles.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    // The latency chain: input → CPU → Windows → GPU/display → network. Each node shows how tuned that stage is.
    public class ChainView : Control, Theme.IDpiSelfScaled
    {
        public List<Stage> Stages = new List<Stage>();
        int hover = -1, focus = 0;
        public ChainView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Height = Theme.S(150); TabStop = true; AccessibleRole = AccessibleRole.Diagram; AccessibleName = "Latency chain";
            // 3.40: while the states load, the placeholder counts them ("Checking tweaks: 120 / 281")
            progressTimer = new System.Windows.Forms.Timer { Interval = 400 };
            progressTimer.Tick += delegate { if (Stages.Count > 0) progressTimer.Stop(); Invalidate(); };
            progressTimer.Start();
            Disposed += delegate { progressTimer.Dispose(); };
        }
        readonly System.Windows.Forms.Timer progressTimer;
        static string LoadingText()
        {
            int done, total; StateCache.Progress(out done, out total);
            return total > 0 ? L.F("Checking tweaks: {0} / {1}", Math.Min(done, total), total) : L.T("Reading the state of every tweak…");
        }
        int Weakest()
        {
            // lowest share tuned; on a tie, the stage with more tweaks still to apply
            int w = -1; double best = 0.999; int missing = -1;
            for (int i = 0; i < Stages.Count; i++)
            {
                var s = Stages[i]; if (s.Total == 0) continue;
                int m = s.Total - s.Tuned;
                if (s.Ratio < best - 1e-9 || (Math.Abs(s.Ratio - best) < 1e-9 && m > missing)) { best = s.Ratio; w = i; missing = m; }
            }
            return w;
        }
        // 3.36: a pipeline in one surface: the stages are stations on one track, read left to right like the signal.
        // It wraps to a second row when the window is too narrow for five readable stations.
        const int MinCol = 150, RowH = 148, Pad = 20;
        int Cols { get { int n = Math.Max(1, Stages.Count); return (Width - 2 - Theme.S(Pad) * 2) / n >= Theme.S(MinCol) ? n : (n + 1) / 2; } }
        int Rows { get { int c = Cols; return Math.Max(1, (Math.Max(1, Stages.Count) + c - 1) / c); } }
        bool RowEnd(int i) { return (i + 1) % Cols == 0; }
        RectangleF Box(int i)
        {
            int cols = Cols; float pad = Theme.S(Pad);
            float w = (Width - 2 - pad * 2) / cols;
            int row = i / cols, col = i % cols;
            return new RectangleF(pad + col * w, row * Theme.S(RowH) + Theme.S(8), w, Theme.S(RowH) - Theme.S(16));
        }
        PointF Node(int i) { var b = Box(i); return new PointF(b.X + Theme.S(12f), b.Y + Theme.S(22f)); }
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified) { }
        protected override void OnResize(EventArgs e)
        {
            int h = Rows * Theme.S(RowH) + Theme.S(6);
            if (Height != h) Height = h;   // the page re-lays itself out on the size change
            base.OnResize(e);
        }
        // 3.3: after a change, one wave runs along the chain to the stage that got better (≈1 s, once)
        int pulseTo = -1; float pulseT;
        public void SetStages(List<Stage> s)
        {
            var prev = Stages;
            if (prev != null && prev.Count == s.Count && Motion.On)
            {
                int to = -1;
                for (int i = 0; i < s.Count; i++) if (s[i].Name == prev[i].Name && s[i].Tuned > prev[i].Tuned) to = i;
                if (to >= 0)
                {
                    pulseTo = to; pulseT = 0;
                    Motion.Run(this, dt => { pulseT += (float)(dt / (double)Theme.Ms.Large); Invalidate(); if (pulseT >= 1) { pulseTo = -1; return true; } return false; });
                }
            }
            Stages = s; OnResize(EventArgs.Empty);
            int w = Weakest();
            AccessibleDescription = string.Join("; ", s.Select(x => x.Name + " " + x.Tuned + " of " + x.Total + " tuned").ToArray()) + (w >= 0 ? ". Weakest link: " + s[w].Name : "");
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1; for (int i = 0; i < Stages.Count; i++) if (Box(i).Contains(e.Location)) h = i;
            if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { if (hover >= 0) Open(hover); base.OnMouseClick(e); }
        void Open(int i) { if (i >= 0 && i < Stages.Count) AppShell.Current.ShowPage(Stages[i].Page); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Right) { focus = Math.Min(Stages.Count - 1, focus + 1); Invalidate(); }
            else if (e.KeyCode == Keys.Left) { focus = Math.Max(0, focus - 1); Invalidate(); }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) Open(focus);
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(BackColor)) g.FillRectangle(b, ClientRectangle);
            var card = new RectangleF(0, 0, Width - 1, Height - Theme.S(6));
            Gfx.Surface(g, card, Theme.Raised, true);
            if (Stages.Count == 0) { TextRenderer.DrawText(g, LoadingText(), Theme.F, Rectangle.Round(RectangleF.Inflate(card, -Theme.S(20), 0)), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left); return; }
            int weak = Weakest();
            float u = Theme.Scale, rad = Theme.S((float)Theme.Radius);
            // hover / keyboard focus: the station's column lights
            for (int i = 0; i < Stages.Count; i++)
                if (i == hover) using (var p = Gfx.Round(RectangleF.Inflate(Box(i), -Theme.S(2), 0), rad)) using (var b = new SolidBrush(Theme.PanelHover)) g.FillPath(b, p);
            // the track: from each station to the next on the same row; a finished stage lights its segment
            for (int i = 0; i < Stages.Count - 1; i++)
            {
                if (RowEnd(i)) continue;
                PointF a = Node(i), z = Node(i + 1);
                bool done = Stages[i].Total > 0 && Stages[i].Tuned >= Stages[i].Total;
                using (var pen = new Pen(done ? Theme.Progress : Theme.Line, Math.Max(1.5f, 1.5f * u))) g.DrawLine(pen, a.X + 9 * u, a.Y, z.X - 9 * u, z.Y);
                // a direction tick just before the next station: the signal runs left to right
                float mx = z.X - 18 * u;
                using (var pen = new Pen(done ? Theme.Progress : Theme.Muted, Math.Max(1.5f, 1.5f * u)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(pen, new[] { new PointF(mx - 2.5f * u, a.Y - 4 * u), new PointF(mx + 2.5f * u, a.Y), new PointF(mx - 2.5f * u, a.Y + 4 * u) });
            }
            var nameF = Theme.Semi(10.5f); var bigF = Theme.Num(24f); var ofF = Theme.Num(12f);
            for (int i = 0; i < Stages.Count; i++)
            {
                var s = Stages[i]; var r = Box(i); var c = Node(i);
                bool done = s.Total > 0 && s.Tuned >= s.Total, isWeak = i == weak;
                Color fill = i == hover ? Theme.PanelHover : Theme.Raised;
                // station: filled when the stage is done, the accent with a soft halo when it is the weakest link
                float nr = 5.5f * u;
                if (isWeak) using (var b = new SolidBrush(Theme.AccentSoft)) g.FillEllipse(b, c.X - nr * 2.1f, c.Y - nr * 2.1f, nr * 4.2f, nr * 4.2f);
                Color nodeC = isWeak ? Theme.Accent : done ? Theme.Progress : s.Tuned > 0 ? Theme.Text : Theme.Muted;
                if (isWeak || done) using (var b = new SolidBrush(nodeC)) g.FillEllipse(b, c.X - nr, c.Y - nr, nr * 2, nr * 2);
                else
                {
                    using (var b = new SolidBrush(fill)) g.FillEllipse(b, c.X - nr, c.Y - nr, nr * 2, nr * 2);
                    using (var pen = new Pen(nodeC, Math.Max(1.5f, 1.5f * u))) g.DrawEllipse(pen, c.X - nr, c.Y - nr, nr * 2, nr * 2);
                }
                if (pulseTo >= 0 && i <= pulseTo)
                {   // the wave from 3.3: each station glows as it passes, the improved one last and longest
                    float cc = pulseT * (pulseTo + 1.4f), al = Math.Max(0f, 1f - Math.Abs(cc - (i + 0.5f)) / (i == pulseTo ? 1.2f : 0.8f));
                    if (al > 0.02f) using (var pen = new Pen(Color.FromArgb((int)(220 * al), Theme.On), 2f * u)) g.DrawEllipse(pen, c.X - nr * 2.2f, c.Y - nr * 2.2f, nr * 4.4f, nr * 4.4f);
                }
                if (isWeak)
                    TextRenderer.DrawText(g, Theme.Cap("Weakest link"), Theme.FT, new Rectangle((int)(c.X + nr * 2.6f), (int)(c.Y - Theme.S(9)), (int)(r.Right - c.X - nr * 2.6f), Theme.S(18)), Theme.AccentInk, fill, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                int x = (int)r.X + Theme.S(4), w = (int)r.Width - Theme.S(20);
                TextRenderer.DrawText(g, L.T(s.Name), nameF, new Rectangle(x, (int)r.Y + Theme.S(42), w, Theme.S(20)), Theme.Strong, fill, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (s.Total == 0) TextRenderer.DrawText(g, "–", bigF, new Point(x, (int)r.Y + Theme.S(64)), Theme.Muted, fill, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                else
                {
                    string t = s.Tuned.ToString(CultureInfo.InvariantCulture);
                    var ts = TextRenderer.MeasureText(g, t, bigF, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    var os = TextRenderer.MeasureText(g, "/" + s.Total, ofF, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    int ny = (int)r.Y + Theme.S(64);
                    TextRenderer.DrawText(g, t, bigF, new Point(x, ny), done ? Theme.Ok : Theme.Strong, fill, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(g, "/" + s.Total, ofF, new Point(x + ts.Width + Theme.S(3), ny + ts.Height - os.Height - Theme.S(3)), Theme.Muted, fill, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
                // share applied: a thin rule, the accent for the weakest link
                var bar = new RectangleF(x, r.Bottom - Theme.S(18), w, Math.Max(2, Theme.S(3)));
                using (var b = new SolidBrush(Gfx.Mix(fill, Theme.Text, 0.12))) g.FillRectangle(b, bar);
                if (s.Ratio > 0) using (var b = new SolidBrush(isWeak ? Theme.Accent : Theme.Progress)) g.FillRectangle(b, bar.X, bar.Y, Math.Max(bar.Height, bar.Width * (float)s.Ratio), bar.Height);
                if (Focused && ShowFocusCues && i == focus) Gfx.FocusRing(g, RectangleF.Inflate(r, -Theme.S(1), Theme.S(2)), rad);
            }
        }
    }

    public enum Vs { Good, Warn, Neutral }

    public class Vital { public string Name, Value; public Vs State; }

    public class VitalRow : Control
    {
        public Vital V;
        public VitalRow(Vital v)
        {
            V = v; Height = 34; Margin = new Padding(0);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AccessibleRole = AccessibleRole.StaticText; AccessibleName = v.Name + ": " + v.Value;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(BackColor)) g.FillRectangle(b, ClientRectangle);
            Color c = V.State == Vs.Good ? Theme.Ok : V.State == Vs.Warn ? Theme.Warn : Theme.AccentOff;
            // 3.36: spec-sheet line: label left, value from a fixed column, the state as a small dot after the value
            int lc = Theme.S(190);
            TextRenderer.DrawText(g, L.T(V.Name), Theme.F, new Rectangle(Theme.S(4), 0, lc - Theme.S(8), Height), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            string val = L.T(V.Value);
            var vs = TextRenderer.MeasureText(g, val, Theme.F, Size.Empty, TextFormatFlags.NoPrefix);
            int vw = Math.Min(vs.Width, Width - lc - Theme.S(24));
            TextRenderer.DrawText(g, val, Theme.F, new Rectangle(lc, 0, vw, Height), V.State == Vs.Warn ? Theme.Strong : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (V.State != Vs.Neutral) using (var b = new SolidBrush(c)) g.FillEllipse(b, lc + vw + Theme.S(6), Height / 2f - Theme.S(3), Theme.S(6), Theme.S(6));
            if (Parent == null || Parent.Controls.GetChildIndex(this) < Parent.Controls.Count - 1) Gfx.Hair(g, 0, Height - 1, Width, Height - 1);
        }
    }

    public class HomePage : Panel, IPage
    {
        ChainView chain; FlowLayoutPanel vitals; Label lastTest, intro; HealthStrip health; StatusTiles tiles;
        DateTime lastRun = DateTime.MinValue; bool running;

        public HomePage()
        {
            BackColor = Theme.Bg; Theme.VerticalOnly(this);
            health = new HealthStrip { Location = new Point(0, 0) };
            tiles = new StatusTiles();   // 3.13: pending, latency and last apply at a glance
            // 3.3: the diagram speaks for itself; the explanation moved into the ⓘ tooltip
            intro = new Label { Text = "Latency chain  ⓘ", Font = Theme.Semi(12f), ForeColor = Theme.Text, AutoSize = false, Height = 26, Location = new Point(0, health.Height + 18), UseMnemonic = false, Cursor = Cursors.Help };
            const string chainHelp = "Every stage between your mouse click and the frame on screen, plus the trip to the game server. Each card counts the tweaks with a real speed effect for that stage that are applied on this PC. Click a stage to open its tweaks.";
            var chainTip = Tips.New(new ToolTip { AutoPopDelay = 20000, InitialDelay = 200 });
            chainTip.SetToolTip(intro, L.T(chainHelp));
            Disposed += delegate { chainTip.Dispose(); };
            chain = new ChainView { Location = new Point(0, intro.Bottom + 4), BackColor = Theme.Bg };

            var vHead = new Label { Text = "This PC right now", Font = Theme.Semi(12f), ForeColor = Theme.Strong, AutoSize = true };
            // 3.36: a spec sheet: the page paints a raised surface just outside the list (see Paint below)
            vitals = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Raised, Padding = new Padding(10, 2, 10, 2) };
            vitals.Controls.Add(new Label { Text = "Reading…", ForeColor = Theme.Muted, AutoSize = true });

            var aHead = new Label { Text = "Quick actions", Font = Theme.Semi(12f), ForeColor = Theme.Strong, AutoSize = true };
            var actions = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg, AutoSize = true };
            Func<Button, Button> wide = b => { b.AutoSize = false; b.Width = 300; b.Height = 40; b.TextAlign = ContentAlignment.MiddleLeft; b.Margin = new Padding(0, 0, 0, 8); var fb = b as FocusButton; if (fb != null) fb.Chevron = true; return b; };
            // 3.33: the main action is on the score card at the top; here only the other one of the pair
            actions.Controls.Add(wide(Mode.Lite ? Theme.Btn("Select the picks for this PC", delegate { MainForm.Instance.SelectAutoPreset(); }) : Theme.Btn("Optimize safely", delegate { MainForm.Instance.OptimizeSafely(); })));
            actions.Controls.Add(wide(Theme.Btn("Measure Sleep(1) precision", delegate { Measure(); })));
            actions.Controls.Add(wide(Theme.Btn("Why is my PC slow right now?", delegate { SlowNowDialog.Check(); })));   // 3.43
            actions.Controls.Add(wide(Theme.Btn("See why: open PC advisor", delegate { AppShell.Current.ShowPage("PC advisor"); })));
            actions.Controls.Add(wide(Theme.Btn("Scan the whole PC", delegate { AppShell.Current.ShowPage("System scan"); })));
            actions.Controls.Add(wide(Theme.Btn("Why ArchTweaker?", delegate { WhyArchTweaker.Show(); })));   // 3.34
            lastTest = new Label { ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = false, Size = new Size(300, 60), Margin = new Padding(0, 4, 0, 0), UseMnemonic = false };
            actions.Controls.Add(lastTest);

            // 3.36: the raised surface around the spec sheet (the list itself has the same fill, so its square corners vanish)
            Paint += (s9, e9) =>
            {
                if (!vitals.Visible) return;
                int m = Theme.S(6);
                Gfx.Surface(e9.Graphics, new RectangleF(vitals.Left - m, vitals.Top - m, vitals.Width + m * 2, vitals.Height + m * 2), Theme.Raised, true);
            };
            vitals.LocationChanged += delegate { Invalidate(); }; vitals.SizeChanged += delegate { Invalidate(); };
            chain.SizeChanged += delegate { OnResize(EventArgs.Empty); };   // one or two rows of cards
            tiles.SizeChanged += delegate { OnResize(EventArgs.Empty); };
            Controls.Add(health); Controls.Add(tiles); Controls.Add(intro); Controls.Add(chain); Controls.Add(vHead); Controls.Add(vitals); Controls.Add(aHead); Controls.Add(actions);
            Resize += delegate
            {
                // 3.2: width without the vertical scrollbar, whether it shows yet or not, so nothing overflows sideways
                int w = ClientSize.Width - (VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth) - Theme.S(4);
                health.Width = w;
                tiles.Top = health.Bottom + Theme.S(Theme.Sp.X8); tiles.Width = w;   // same width as the health panel above
                intro.Width = TextRenderer.MeasureText(intro.Text, intro.Font).Width + Theme.S(8);
                intro.Top = tiles.Bottom + Theme.S(20);
                chain.Top = intro.Bottom + Theme.S(8);
                chain.Width = w;
                int colR = Theme.S(320), colL = Math.Max(Theme.S(360), w - colR - Theme.S(24));
                int sm = Theme.S(6);   // room for the spec sheet's surface edge and shadow
                vHead.Location = new Point(0, chain.Bottom + Theme.S(24));
                vitals.Location = new Point(sm, vHead.Bottom + Theme.S(10) + sm); vitals.Width = colL - sm * 2;
                foreach (Control c in vitals.Controls) c.Width = vitals.Width - vitals.Padding.Horizontal - 2;
                vitals.Height = vitals.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical) + vitals.Padding.Vertical;
                aHead.Location = new Point(colL + Theme.S(24), chain.Bottom + Theme.S(24));
                actions.Location = new Point(colL + Theme.S(24), aHead.Bottom + Theme.S(10));
                Invalidate();
            };
            ShowLast();
        }

        public void OnShow() { if ((DateTime.Now - lastRun).TotalSeconds > 5) Refresh2(); else RefreshTiles(); }
        public void RefreshTiles() { if (IsDisposed) return; tiles.Set(StatusTiles.ForHome()); }
        public void Filter(string q) { }

        void ShowLast()
        {
            object o = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "LastSleepTest");
            lastTest.Text = o == null ? "No Sleep(1) measurement yet. Run one before and after applying tweaks to see the difference." : "Last Sleep(1) measurement: " + o;
        }

        void Measure()
        {
            string r = null;
            AppShell.Current.Busy("Measuring Sleep(1) × 500…", () => { r = LatencyTest.SleepPrecision(500); }, () => { Log.W(r); ShowLast(); Refresh2(); });
        }

        public void Refresh2()
        {
            if (running) return;
            running = true; lastRun = DateTime.Now;
            List<Stage> st = null; List<Vital> vs = null; HealthInfo hi = null;
            bool restart = MainForm.Instance != null && MainForm.Instance.RestartPending;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { hi = Health.Read(restart); } catch (Exception ex) { Log.W("Home health: {0}", ex.Message); }
                try { st = Stages(); } catch (Exception ex) { Log.W("Home: {0}", ex.Message); }
                try { vs = Vitals(); } catch (Exception ex) { Log.W("Home: {0}", ex.Message); }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        running = false;
                        if (hi != null) health.Set(hi);
                        tiles.Set(StatusTiles.ForHome());
                        if (st != null) chain.SetStages(st);
                        if (vs != null)
                        {
                            vitals.SuspendLayout();
                            foreach (Control c in vitals.Controls.Cast<Control>().ToList()) c.Dispose();
                            vitals.Controls.Clear();
                            foreach (var v in vs) vitals.Controls.Add(new VitalRow(v) { BackColor = Theme.Raised });
                            vitals.ResumeLayout();
                            OnResize(EventArgs.Empty);
                        }
                    }));
                }
                catch { running = false; }
            });
        }

        static readonly string[] InputIds = { "mouseaccel", "pp_usb", "keyrepeat" };
        static readonly string[] GpuIds = { "hags", "windowedopt", "fso", "mpo", "gamedvr", "gamebar", "pp_gpu", "pp_pcie" };
        static readonly string[] NetIds = { "gd_nicpower", "gd_wifi_roam", "pp_wifi" };

        static List<Stage> Stages()
        {
            var defs = new List<KeyValuePair<Stage, Func<Tweak, bool>>>
            {
                new KeyValuePair<Stage, Func<Tweak, bool>>(new Stage { Name = "Input", Page = Cat.Input }, t => t.Category == Cat.Input || InputIds.Contains(t.Id)),
                new KeyValuePair<Stage, Func<Tweak, bool>>(new Stage { Name = "CPU & power", Page = Cat.PowerPlan }, t => t.Category == Cat.PowerPlan && !InputIds.Contains(t.Id) && !GpuIds.Contains(t.Id) && !NetIds.Contains(t.Id)),
                new KeyValuePair<Stage, Func<Tweak, bool>>(new Stage { Name = "Windows", Page = Cat.Responsiveness }, t => (t.Category == Cat.System || t.Category == Cat.Responsiveness || t.Category == Cat.TasksServices || t.Category == Cat.Commands || t.Category == Cat.Gaming) && !GpuIds.Contains(t.Id) && !InputIds.Contains(t.Id)),
                new KeyValuePair<Stage, Func<Tweak, bool>>(new Stage { Name = "GPU & display", Page = Cat.Gpu }, t => (t.Category == Cat.Gpu && !NetIds.Contains(t.Id)) || GpuIds.Contains(t.Id)),
                new KeyValuePair<Stage, Func<Tweak, bool>>(new Stage { Name = "Network", Page = Cat.Network }, t => t.Category == Cat.Network || NetIds.Contains(t.Id)),
            };
            var list = new List<Stage>();
            foreach (var d in defs)
            {
                var ts = Catalog.All.Where(t => t.Impact >= 1 && t.Risk != Risk.Advanced && t.Applicable && Mode.Allows(t) && d.Value(t)).ToList();
                d.Key.Total = ts.Count;
                d.Key.Tuned = ts.Count(StateCache.Applied);   // 3.6.4: cached
                list.Add(d.Key);
            }
            return list;
        }

        static List<Vital> Vitals()
        {
            var v = new List<Vital>();
            // 3.40: the power API instead of a powercfg.exe process (and its localised text)
            var act = PowerExplorer.Schemes().Where(s => s.Active).ToList();
            string pn = act.Count > 0 ? act[0].Name : "";
            string plan = act.Count > 0 ? act[0].Id.ToString() + " (" + pn + ")" : "";
            bool perfPlan = pn.IndexOf("ArchTweaker", StringComparison.OrdinalIgnoreCase) >= 0 || plan.IndexOf(Catalog.MAXPLAN, StringComparison.OrdinalIgnoreCase) >= 0 || plan.IndexOf("e9a42b02", StringComparison.OrdinalIgnoreCase) >= 0 || plan.IndexOf("8c5e7fda", StringComparison.OrdinalIgnoreCase) >= 0;
            v.Add(new Vital { Name = "Power plan", Value = pn.Length > 0 ? pn : "unknown", State = pn.Length == 0 ? Vs.Neutral : perfPlan ? Vs.Good : Vs.Warn });

            double ms = TimerRes.CurrentMs();
            v.Add(new Vital { Name = "Timer resolution", Value = ms <= 0 ? "unknown" : ms.ToString("0.###") + " ms" + (ms > 10 ? " (Windows default)" : ""), State = ms <= 0 ? Vs.Neutral : ms <= 0.6 ? Vs.Good : ms <= 1.1 ? Vs.Neutral : Vs.Warn });

            bool dt = Probe.BcdYes("disabledynamictick");
            v.Add(new Vital { Name = "Dynamic tick", Value = dt ? "Off" : "On (Windows default)", State = dt ? Vs.Good : Vs.Neutral });

            object hags = Reg.Get(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
            v.Add(new Vital { Name = "GPU scheduling (HAGS)", Value = hags is int && (int)hags == 2 ? "On" : "Off", State = hags is int && (int)hags == 2 ? Vs.Good : Vs.Neutral });

            var dvr = Catalog.All.FirstOrDefault(t => t.Id == "gamedvr");
            bool dvrOff = dvr != null && StateCache.Applied(dvr);
            v.Add(new Vital { Name = "Background recording", Value = dvrOff ? "Off" : "On (Game DVR)", State = dvrOff ? Vs.Good : Vs.Warn });

            var vbs = HwProbe.VbsRunning();
            v.Add(new Vital { Name = "Virtualization security", Value = vbs == null ? "unknown" : vbs.Value ? "Running" : "Off", State = vbs == null ? Vs.Neutral : vbs.Value ? Vs.Warn : Vs.Good });

            var links = HwProbe.Links(); var up = links.FirstOrDefault(x => x.Up);
            bool freePort = links.Any(x => !x.Wireless && !x.Up);
            v.Add(new Vital { Name = "Network", Value = up == null ? "offline" : (up.Wireless ? "Wi-Fi" : "Ethernet") + (up.Mbps > 0 ? ", " + (up.Mbps >= 1000 ? (up.Mbps / 1000.0).ToString("0.#") + " Gbps" : up.Mbps + " Mbps") : "") + (up.Wireless && freePort ? " (wired port unused)" : ""), State = up == null ? Vs.Neutral : up.Wireless ? (freePort ? Vs.Warn : Vs.Neutral) : Vs.Good });

            var ram = HwProbe.RamSpeed();
            v.Add(new Vital { Name = "Memory", Value = ram == null || ram[0] == 0 ? "unknown" : ram[0] + " MT/s (rated " + ram[1] + ")", State = ram == null || ram[0] == 0 ? Vs.Neutral : ram[0] + 50 < ram[1] ? Vs.Warn : Vs.Good });

            var hz = HwProbe.Refresh();
            v.Add(new Vital { Name = "Display", Value = hz == null ? "unknown" : hz[0] + " Hz" + (hz[1] > hz[0] + 5 ? " (supports " + hz[1] + ")" : ""), State = hz == null ? Vs.Neutral : hz[1] > hz[0] + 5 ? Vs.Warn : Vs.Good });
            return v;
        }
    }

    // ================= HOME STATUS TILES =================
    // A row of glanceable numbers (PowerToys / Windows Settings home pattern). A tile with an action is a button.
    public class StatusTiles : Control
    {
        // laid out in S() units: the page-wide DPI scale pass must not scale it again
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified) { }
        protected override bool ScaleChildren { get { return false; } }
        public class Tile { public string Title, Value, Sub; public Color Tone; public Action Run; }
        List<Tile> tiles = new List<Tile>(); readonly List<Rectangle> rects = new List<Rectangle>();
        int hover = -1, focus = -1; bool keys;
        public StatusTiles()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Bg; TabStop = true; Height = Theme.S(92);
            AccessibleRole = AccessibleRole.Grouping; AccessibleName = L.T("Status");
        }
        public void Set(List<Tile> t) { tiles = t; DoLayout(); Invalidate(); Announce(); }
        // 3.36: one surface split into columns by hairlines; wraps to more rows only when a column would get too narrow
        void DoLayout()
        {
            rects.Clear(); if (tiles.Count == 0) return;
            int minW = Theme.S(200), h = Theme.S(92);
            int perRow = Math.Max(1, Math.Min(tiles.Count, Width / minW));
            int rows = (tiles.Count + perRow - 1) / perRow;
            for (int i = 0; i < tiles.Count; i++)
            {
                int col = i % perRow, row = i / perRow;
                int x0 = (Width - 1) * col / perRow, x1 = (Width - 1) * (col + 1) / perRow;
                rects.Add(new Rectangle(x0, row * h, x1 - x0, h));
            }
            int want = rows * h + Theme.S(6);
            if (Height != want) Height = want;
        }
        protected override void OnResize(EventArgs e) { DoLayout(); base.OnResize(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            if (rects.Count == 0) return;
            var card = new RectangleF(0, 0, Width - 1, rects[rects.Count - 1].Bottom);
            Gfx.Surface(g, card, Theme.Raised, true);
            float rad = Theme.S((float)Theme.Radius);
            for (int i = 0; i < tiles.Count && i < rects.Count; i++)
            {
                var t = tiles[i]; var r = rects[i]; bool hot = i == hover && t.Run != null;
                Color fill = hot ? Theme.PanelHover : Theme.Raised;
                if (hot)
                {
                    var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var p = Gfx.Round(new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), rad)) using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                    g.SmoothingMode = old;
                }
                if (r.X > 0) Gfx.Hair(g, r.X, r.Y + Theme.S(16), r.X, r.Bottom - Theme.S(16));
                if (r.Y > 0) Gfx.Hair(g, r.X + Theme.S(16), r.Y, r.Right - Theme.S(16), r.Y);
                int px = Theme.S(20), x = r.X + px;
                bool tone = t.Tone != Theme.Border;   // Border = nothing to report
                if (tone)
                {
                    var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var b = new SolidBrush(t.Tone)) g.FillEllipse(b, x, r.Y + Theme.S(19), Theme.S(7), Theme.S(7));
                    g.SmoothingMode = old;
                }
                int tx = tone ? x + Theme.S(13) : x;
                TextRenderer.DrawText(g, Theme.CapFit(t.Title, Theme.FT, r.Right - px - tx), Theme.FT, new Rectangle(tx, r.Y + Theme.S(14), r.Right - px - tx, Theme.S(16)), Theme.Muted, fill, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, t.Value, Theme.Num(18f), new Rectangle(x - Theme.S(1), r.Y + Theme.S(32), r.Width - px * 2, Theme.S(30)), Theme.Strong, fill, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                string sub = (t.Sub ?? "") + (t.Run != null ? "  \u203A" : "");
                TextRenderer.DrawText(g, sub, Theme.FS, new Rectangle(x, r.Y + Theme.S(64), r.Width - px * 2, Theme.S(16)), t.Run != null ? (hot ? Theme.Strong : Theme.Text) : Theme.Muted, fill, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (keys && Focused && i == focus) Gfx.FocusRing(g, RectangleF.Inflate(r, -2, -2), rad);
            }
        }
        int At(Point p) { for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(p)) return i; return -1; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = At(e.Location);
            if (h != hover) { hover = h; Cursor = h >= 0 && tiles[h].Run != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { int h = At(e.Location); if (h >= 0 && e.Button == MouseButtons.Left) Run(h); base.OnMouseClick(e); }
        void Run(int i) { if (i >= 0 && i < tiles.Count && tiles[i].Run != null) tiles[i].Run(); }
        protected override void OnGotFocus(EventArgs e) { if (focus < 0) focus = 0; Invalidate(); Announce(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { keys = false; Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int n = tiles.Count; if (n == 0) { base.OnKeyDown(e); return; }
            keys = true;
            if (e.KeyCode == Keys.Right) { focus = (focus + 1) % n; e.Handled = true; }
            else if (e.KeyCode == Keys.Left) { focus = (focus - 1 + n) % n; e.Handled = true; }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { Run(Math.Max(0, focus)); e.Handled = true; }
            if (e.Handled) { Invalidate(); Announce(); }
            base.OnKeyDown(e);
        }
        void Announce()
        {
            if (!Focused || focus < 0 || focus >= tiles.Count) return;
            var t = tiles[focus];
            AccessibleName = L.T(t.Title) + ": " + t.Value + ". " + (t.Sub ?? "");
            AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }

        // the tiles Home shows: what the health strip above doesn't (Pending, Latency, Last apply). Cached state only.
        public static List<Tile> ForHome()
        {
            var m = MainForm.Instance;
            int pend = m != null ? m.Pending.Count : 0;
            var tiles = new List<Tile>
            {
                new Tile { Title = "Pending", Value = pend.ToString(), Sub = L.T(pend > 0 ? "Review and apply" : "Nothing waits for Apply"), Tone = pend > 0 ? Theme.Warn : Theme.Border,
                    Run = pend > 0 ? (Action)(() => m.ReviewPending()) : null },
            };
            var all = LatencyRuns.All(); var last = all.LastOrDefault();
            if (last == null) tiles.Add(new Tile { Title = "Latency", Value = "—", Sub = L.T("Find slow drivers"), Tone = Theme.Border, Run = () => m.ShowPage("Latency") });
            else
            {
                string sub = L.T("worst DPC/ISR");
                if (all.Count > 1)
                {
                    var prev = all[all.Count - 2];
                    if (prev.WorstUs > 0) { double d = (last.WorstUs - prev.WorstUs) / prev.WorstUs * 100; sub = (Math.Abs(d) < 10 ? "≈ " : d < 0 ? "▼ " : "▲ ") + Math.Abs(d).ToString("0") + " % " + L.T("since the scan before"); }
                }
                int lvl = DriverTrace.Level(last.WorstUs);
                tiles.Add(new Tile { Title = "Latency", Value = last.WorstUs.ToString("0") + " µs", Sub = sub, Tone = lvl == 2 ? Theme.Bad : lvl == 1 ? Theme.Warn : Theme.Ok, Run = () => m.ShowPage("Latency") });
            }
            var h = History.Entries().FirstOrDefault();
            Action openHistory = () => { m.ShowPage("Tools & backup"); var tp = m.PageOf("Tools & backup") as ToolsPage; if (tp != null) tp.JumpTo("Backup & restore"); };
            if (h == null) tiles.Add(new Tile { Title = "Last apply", Value = "—", Sub = L.T("Nothing applied yet"), Tone = Theme.Border });
            else tiles.Add(new Tile { Title = "Last apply", Value = h.When.ToString("dd MMM HH:mm"), Sub = h.Items.Count == 1 ? L.T("1 change · Apply history") : L.F("{0} changes · Apply history", h.Items.Count), Tone = Theme.On, Run = openHistory });
            return tiles;
        }
    }
}
