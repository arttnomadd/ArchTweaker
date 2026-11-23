// ArchTweaker 1.5 page: PC advisor.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    // One finding. The left stripe's colour is the severity, and the label repeats it in words, so colour is never the only signal.
    public class FindingCard : Panel
    {
        readonly Finding f;
        readonly Label lvl, title, body;
        readonly Button act;
        public FindingCard(Finding f)
        {
            this.f = f;
            BackColor = Theme.Panel; Margin = new Padding(0, 0, 0, 8); Padding = new Padding(0);
            Color c = LevelColor(f.Level);
            // 3.36: a raised card; the level is a dot and a caption label in its colour (no coloured edge stripe)
            DoubleBuffered = true;
            lvl = new Label { Text = Theme.Cap(LevelText(f.Level)), ForeColor = f.Level == Level.Fix ? Theme.AccentInk : c, Font = Theme.FT, AutoSize = true, Tag = "notr" };
            title = new Label { Text = f.Title, ForeColor = Theme.Strong, Font = Theme.Semi(11f), AutoSize = true };
            body = new Label { Text = f.Text, ForeColor = Theme.Muted, Font = Theme.F, AutoSize = false };
            Controls.Add(lvl); Controls.Add(title); Controls.Add(body);
            var pend = f.Pending;
            if (pend.Count > 0)
            {
                act = Theme.Btn(pend.Count == 1 ? "Select 1 tweak" : "Select " + pend.Count + " tweaks", delegate
                {
                    AppShell.Current.SelectIds(pend.Select(t => t.Id));
                    act.Text = "Selected"; act.Enabled = false;
                    Log.W("Advisor: selected {0} for '{1}'. Review on the tweak pages, then click Apply.", string.Join(", ", pend.Select(t => t.Title).ToArray()), f.Title);
                });
                var tip = Tips.New(); tip.SetToolTip(act, L.T(string.Join("\n", pend.Select(t => "• " + t.Title).ToArray())));
                Controls.Add(act);
            }
        }
        public static Color LevelColor(Level l) { return l == Level.Fix ? Theme.Accent : l == Level.Tip ? Theme.Warn : l == Level.Good ? Theme.Ok : Theme.Muted; }
        static string LevelText(Level l) { return l == Level.Fix ? "Biggest gain" : l == Level.Tip ? "Worth doing" : l == Level.Good ? "Already good" : "Note"; }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Gfx.Surface(g, new RectangleF(0, 0, Width - 1, Height - Theme.S(3)), Theme.Panel, true);
            var o = g.SmoothingMode; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(LevelColor(f.Level))) g.FillEllipse(b, lvl.Left - Theme.S(14), lvl.Top + (lvl.Height - Theme.S(7)) / 2f, Theme.S(7), Theme.S(7));
            g.SmoothingMode = o;
        }
        public void Fit(int width)
        {
            Width = width;
            int x = 26, right = act != null ? act.PreferredSize.Width + 24 : 0;
            int tw = Math.Max(200, width - x - 16 - right);
            lvl.BackColor = title.BackColor = body.BackColor = Theme.Panel;
            lvl.Location = new Point(x, 12);
            title.Location = new Point(x, lvl.Bottom + 2); title.MaximumSize = new Size(tw, 0);
            Size bs = TextRenderer.MeasureText(f.Text, Theme.F, new Size(tw, 0), TextFormatFlags.WordBreak);
            body.Location = new Point(x, title.Bottom + 4); body.Size = new Size(tw, bs.Height + 2);
            Height = body.Bottom + 14 + Theme.S(3);   // 3.36: room for the card's shadow
            if (act != null) act.Location = new Point(width - act.PreferredSize.Width - 16, (Height - Theme.S(3) - act.Height) / 2);   // centred above the shadow
        }
    }

    public class AdvisorPage : Panel, IPage
    {
        FlowLayoutPanel list; Label summary; Button selectAll; List<Finding> findings;
        public AdvisorPage()
        {
            BackColor = Theme.Bg;
            list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(0, 4, 0, 0) };
            list.Resize += delegate { FitAll(); };
            summary = new Label { Dock = DockStyle.Top, Height = 30, ForeColor = Theme.Text, Font = Theme.F, Padding = new Padding(2, 6, 0, 0), Text = "Reading your hardware…" };
            var bar = Theme.Bar();
            selectAll = Theme.Lead("Select all recommended", delegate { SelectAll(); });
            bar.Controls.Add(selectAll);
            bar.Controls.Add(Theme.Btn("Check again", delegate { Run(); }));
            Controls.Add(list);
            Controls.Add(Theme.Note("Reads your CPU layout, network link, RAM speed, display, storage and security state, then picks the tweaks that matter for THIS hardware. Selecting only ticks the boxes. Nothing changes until you click Apply."));
            Controls.Add(summary);
            Controls.Add(bar);
        }
        bool done;
        public void OnShow() { if (!done) Run(); }
        public void Filter(string q)
        {
            foreach (FindingCard c in list.Controls.OfType<FindingCard>())
                c.Visible = q.Length == 0 || (c.Tag as Finding).Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (c.Tag as Finding).Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void Run()
        {
            done = true;
            List<Finding> r = null;
            AppShell.Current.LoadData("Checking this PC…", () => { HwProbe.Invalidate(); PowerCache.Invalidate(); r = Advisor.Run(); }, () => { findings = r; Fill(); });
        }

        void Fill()
        {
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) c.Dispose();
            list.Controls.Clear();
            foreach (var f in findings.OrderBy(x => x.Level == Level.Fix ? 0 : x.Level == Level.Tip ? 1 : x.Level == Level.Info ? 2 : 3))
                list.Controls.Add(new FindingCard(f) { Tag = f });
            FitAll();
            list.ResumeLayout();
            int n = findings.SelectMany(x => x.Pending).Distinct().Count();
            int fix = findings.Count(x => x.Level == Level.Fix);
            summary.Text = Advisor.Summary + ".";
            selectAll.Text = n > 0 ? "Select all " + n + " recommended" : "Nothing left to select";
            selectAll.Enabled = n > 0;
            Log.W("Advisor: {0} findings, {1} with the biggest gain, {2} tweaks recommended for this PC.", findings.Count, fix, n);
        }

        void FitAll()
        {
            int w = list.ClientSize.Width - list.Padding.Horizontal - (list.VerticalScroll.Visible ? 2 : SystemInformation.VerticalScrollBarWidth + 2);
            foreach (var c in list.Controls.OfType<FindingCard>()) c.Fit(Math.Max(420, Math.Min(Theme.S(Theme.MaxContent), w)));
        }

        void SelectAll()
        {
            if (findings == null) return;
            var ids = findings.SelectMany(x => x.Pending).Select(t => t.Id).Distinct().ToList();
            AppShell.Current.SelectIds(ids);
            Log.W("Advisor: {0} tweaks selected for this PC. Review them on the tweak pages, then click Apply.", ids.Count);
        }
    }
}
