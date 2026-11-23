// ArchTweaker 1.7: Hosts blocker page (online lists, allowlist, size meter, domain checker).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    // How big the hosts file will be, against what Windows' DNS Client handles comfortably.
    public class SizeMeter : Control
    {
        public int Count; public bool Exact;
        public const int Comfortable = 50000, Heavy = 150000;
        public SizeMeter()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 52; AccessibleRole = AccessibleRole.ProgressBar;
        }
        public static string Verdict(int n)
        {
            return n <= Comfortable ? "Comfortable size for Windows" : n <= Heavy ? "Large: watch for slow first lookups after boot" : "Very large: Windows' DNS Client may use high CPU and lookups can stall. For lists this big, the AdGuard DNS tweak (Internet & Network) blocks at the DNS server instead.";
        }
        public void Set(int n, bool exact) { Count = n; Exact = exact; AccessibleName = (exact ? "" : "About ") + n.ToString("N0") + " domains. " + Verdict(n); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(BackColor)) g.FillRectangle(b, ClientRectangle);
            Color c = Count <= Comfortable ? Theme.Ok : Count <= Heavy ? Theme.Warn : Theme.Bad;
            string big = (Exact ? "" : "≈ ") + Count.ToString("N0") + " domains";
            var bf = Theme.Semi(13f);
            var sz = TextRenderer.MeasureText(big, bf);
            TextRenderer.DrawText(g, big, bf, new Point(0, 0), Theme.Strong, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, L.T(Verdict(Count)), Theme.F, new Point(sz.Width + 8, 4), c, TextFormatFlags.NoPrefix);
            // scale: 0 … 200k, with the two thresholds marked
            float w = Width - 2, y = 32, max = 200000f;
            using (var p = Gfx.Round(new RectangleF(0, y, w, 6), 3)) using (var b = new SolidBrush(Theme.AccentOff)) g.FillPath(b, p);
            float fw = Math.Max(6, Math.Min(1f, Count / max) * w);
            using (var p = Gfx.Round(new RectangleF(0, y, fw, 6), 3)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
            foreach (var t in new[] { Comfortable, Heavy })
            {
                float x = t / max * w;
                using (var pen = new Pen(Theme.Muted)) g.DrawLine(pen, x, y - 3, x, y + 9);
                TextRenderer.DrawText(g, (t / 1000) + "k", Theme.FS, new Point((int)x - 10, (int)y + 8), Theme.Muted, TextFormatFlags.NoPrefix);
            }
        }
    }

    public class SourceCard : Panel
    {
        public HostSource S; public Toggle Tog; Label name, desc, meta, caution; bool hasCaution;
        public SourceCard(HostSource s, bool on)
        {
            S = s; BackColor = Theme.Panel; Margin = new Padding(0, 0, 0, 6); hasCaution = s.Caution != null;
            DoubleBuffered = true;   // 3.38: a raised card (Gfx.Surface); the switch shows on / off, no edge stripe
            Tog = new Toggle { Location = new Point(14, 12), AccessibleName = s.Name };
            Tog.Checked = on;
            name = new Label { Text = s.Name, Font = Theme.FB, ForeColor = Theme.Text, AutoSize = true, Location = new Point(66, 12), UseMnemonic = false, Cursor = Cursors.Hand };
            desc = new Label { Text = s.Desc, ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = false, UseMnemonic = false };
            meta = new Label { ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = true, UseMnemonic = false };
            caution = new Label { Text = s.Caution ?? "", ForeColor = Theme.Warn, Font = Theme.FS, AutoSize = false, UseMnemonic = false, Visible = s.Caution != null };
            Controls.Add(Tog); Controls.Add(name); Controls.Add(desc); Controls.Add(meta); Controls.Add(caution);
            name.Click += delegate { Tog.Checked = !Tog.Checked; };
            SetMeta(-1);
            Resize += delegate { Fit(); };
        }
        public void SetMeta(int cached)
        {
            var parts = new List<string>();
            if (S.Builtin != null) parts.Add(S.Builtin.Length + " domains, built in");
            else if (cached >= 0) parts.Add(cached.ToString("N0") + " domains in the last download (" + HostsEngine.CachedAt(S.Id).ToString("yyyy-MM-dd") + ")");
            else parts.Add(S.Approx > 0 ? "≈ " + S.Approx.ToString("N0") + " domains" : "size shown after the first download");
            // 3.38: the list's own date, and a warning when its maintainer stopped updating it
            DateTime? ld = S.Builtin == null && cached >= 0 ? HostsEngine.CachedListDate(S.Id) : null;
            if (ld.HasValue) parts.Add(L.F("list dated {0}", ld.Value.ToString("yyyy-MM-dd")));
            bool stale = HostsEngine.Stale(ld, DateTime.Now);
            string warn = stale ? L.F("Its maintainer has not updated this list since {0}. It still works, but new ad and tracking servers are missing.", ld.Value.ToString("yyyy-MM-dd")) : null;
            string ctext = S.Caution == null ? warn : warn == null ? L.T(S.Caution) : L.T(S.Caution) + " " + warn;
            hasCaution = ctext != null; caution.Text = ctext ?? ""; caution.Visible = hasCaution; Fit();
            if (S.License != null) parts.Add(S.License);
            if (S.Home != null) parts.Add(S.Home.Replace("https://", ""));
            meta.Text = string.Join("  ·  ", parts.ToArray());
        }
        void Fit()
        {
            int x = 66, w = Math.Max(150, Width - x - 14);
            name.MaximumSize = new Size(w, 0);
            desc.Location = new Point(x, name.Bottom + 3); desc.Width = w;
            desc.Height = TextRenderer.MeasureText(desc.Text, desc.Font, new Size(w, 1000), TextFormatFlags.WordBreak).Height + 1;
            int y = desc.Bottom + 3;
            if (hasCaution) { caution.Location = new Point(x, y); caution.Width = w; caution.Height = TextRenderer.MeasureText(caution.Text, caution.Font, new Size(w, 1000), TextFormatFlags.WordBreak).Height + 1; y = caution.Bottom + 2; }
            meta.MaximumSize = new Size(w, 0);   // 3.42: wraps on a narrow window instead of running past the card
            meta.Location = new Point(x, y);
            Height = meta.Bottom + 12 + Theme.S(3);   // room for the card's shadow
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Gfx.Surface(e.Graphics, new RectangleF(0, 0, Width - 1, Height - Theme.S(3)), Theme.Panel, true);
        }
    }

    public class HostsPage : Panel, IPage
    {
        HostsConfig cfg; readonly List<SourceCard> cards = new List<SourceCard>();
        FlowLayoutPanel list; Panel right; SizeMeter meter; Label state, sub, checkResult;
        TextBox allow, custom, check; CheckBox compact, gaming, skipMs, auto;
        readonly Dictionary<string, int> cachedCounts = new Dictionary<string, int>();

        public HostsPage()
        {
            BackColor = Theme.Bg;
            cfg = HostsConfig.Load();

            var bar = Theme.Bar();
            bar.Controls.Add(Theme.Lead("Download lists and apply", delegate { Run(true); }));
            bar.Controls.Add(Theme.Btn("Apply from saved copies", delegate { Run(false); }));
            bar.Controls.Add(Theme.Btn("Remove block", delegate { AppShell.Current.Busy("Removing the hosts block…", HostsModule.Remove, () => { auto.Checked = false; Refresh2(); }); }));
            bar.Controls.Add(Theme.Btn("Open hosts in Notepad", delegate { Shell.OpenVisible("notepad.exe", HostsModule.File_); }));
            state = new Label { AutoSize = true, Font = Theme.FB, Margin = new Padding(12, 8, 0, 0), UseMnemonic = false };
            bar.Controls.Add(state);

            var top = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Theme.Bg, Padding = new Padding(0, 6, 0, 6) };
            meter = new SizeMeter { Dock = DockStyle.Top, BackColor = Theme.Bg };
            sub = new Label { Dock = DockStyle.Top, Height = 22, ForeColor = Theme.Muted, Font = Theme.FS, UseMnemonic = false, AutoEllipsis = true };
            top.Controls.Add(sub); top.Controls.Add(meter);

            // ---- sources (left)
            list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(0, 0, 8, 0) };
            foreach (var g in HostSources.Groups)
            {
                list.Controls.Add(new Label { Text = g, Font = Theme.FB, ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(0, list.Controls.Count == 0 ? 0 : 12, 0, 6), UseMnemonic = false });
                foreach (var s in HostSources.All.Where(x => x.Group == g))
                {
                    var c = new SourceCard(s, cfg.Sources.Contains(s.Id));
                    c.Tog.CheckedChanged += delegate { SyncSources(); };
                    cards.Add(c); list.Controls.Add(c);
                }
            }
            list.HorizontalScroll.Maximum = 0; list.HorizontalScroll.Visible = false;
            list.Resize += delegate { int w = list.ClientSize.Width - list.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4; foreach (var c in cards) c.Width = Math.Max(300, w); };

            // ---- options (right)
            right = new Panel { Dock = DockStyle.Right, Width = 380, BackColor = Theme.Panel, Padding = new Padding(16, 12, 16, 12), AutoScroll = true };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Panel };
            Func<string, Label> head = t => new Label { Text = t, Font = Theme.FB, ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, 10, 0, 4), UseMnemonic = false };
            Func<string, Label> note = t => new Label { Text = t, ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = true, MaximumSize = new Size(320, 0), Margin = new Padding(0, 0, 0, 4), UseMnemonic = false };
            Func<string, bool, CheckBox> opt = (t, v) => new TickBox { Text = t, Checked = v, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, AutoSize = true, MaximumSize = new Size(320, 0), Margin = new Padding(0, 2, 0, 2), UseMnemonic = false };

            flow.Controls.Add(head("Options"));
            compact = opt("Compact file: 9 domains per line", cfg.Compact);
            gaming = opt("Never block game launchers & voice chat", cfg.GamingSafe);
            skipMs = opt("Leave out Microsoft domains (no Defender alert)", cfg.SkipMicrosoft);
            auto = opt("Update the lists every Sunday automatically", cfg.AutoUpdate);
            foreach (var cb in new[] { compact, gaming, skipMs, auto }) { cb.CheckedChanged += delegate { SyncOptions(); }; flow.Controls.Add(cb); }
            var tip = Tips.New();
            tip.SetToolTip(gaming, L.T(string.Join(", ", HostsEngine.Gaming)));
            tip.SetToolTip(compact, L.T("Same blocking, about 9× fewer lines. Easier on Windows' DNS Client."));
            tip.SetToolTip(auto, L.T("A scheduled task runs ArchTweaker /hostsupdate weekly, but only while the block is active."));

            flow.Controls.Add(head("Check a domain"));
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Panel, Margin = new Padding(0) };
            check = new TextBox { Width = 240, BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 2, 8, 0) };
            check.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Check(); } };
            row.Controls.Add(check); row.Controls.Add(Theme.Btn("Check domain", delegate { Check(); }));
            flow.Controls.Add(row);
            checkResult = note("Type a site (for example ads.example.com) to see whether it's blocked and by which list.");
            flow.Controls.Add(checkResult);

            flow.Controls.Add(head("Allowlist"));
            flow.Controls.Add(note("Never blocked, including all their subdomains. One per line."));
            allow = Box(cfg.Allow); flow.Controls.Add(allow);
            flow.Controls.Add(head("My blocked domains"));
            flow.Controls.Add(note("Always blocked, even if they're on an allowlist. One per line."));
            custom = Box(cfg.Custom); flow.Controls.Add(custom);
            flow.Controls.Add(head("Always allowed"));
            flow.Controls.Add(note("So the blocker can't break Windows Update, the network-status icon, Microsoft sign-in or its own list downloads: " + string.Join(", ", HostsEngine.Safety) + "."));
            flow.Controls.Add(note("Tip: if a blocked site still loads in Chrome, Edge or Firefox, the browser may be using its own secure DNS. Turn it off in the browser, or tick 'Block browsers' encrypted DNS'."));
            right.Controls.Add(flow);

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            body.Controls.Add(list); body.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 12, BackColor = Theme.Bg }); body.Controls.Add(right);
            Controls.Add(body); Controls.Add(top); Controls.Add(bar);
        }

        TextBox Box(List<string> lines)
        {
            var t = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Width = 340, Height = 96, BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Mono, Text = string.Join(Environment.NewLine, lines.ToArray()) };
            t.Leave += delegate { SyncLists(); };
            return t;
        }

        void SyncSources() { cfg.Sources = cards.Where(c => c.Tog.Checked).Select(c => c.S.Id).ToList(); cfg.Save(); UpdateMeter(); }
        void SyncLists() { cfg.Allow = allow.Lines.ToList(); cfg.Custom = custom.Lines.ToList(); cfg.Save(); UpdateMeter(); }
        void SyncOptions()
        {
            cfg.Compact = compact.Checked; cfg.GamingSafe = gaming.Checked; cfg.SkipMicrosoft = skipMs.Checked;
            bool autoChanged = cfg.AutoUpdate != auto.Checked; cfg.AutoUpdate = auto.Checked; cfg.Save();
            if (autoChanged)
            {
                bool on = cfg.AutoUpdate;
                AppShell.Current.Busy(on ? "Creating the weekly update task…" : "Removing the weekly update task…", () => { if (on) HostsEngine.InstallTask(); else HostsEngine.RemoveTask(); }, Refresh2);
            }
        }

        void UpdateMeter()
        {
            int n = 0;
            foreach (var id in cfg.Sources)
            {
                var s = HostSources.Get(id); if (s == null) continue;
                int c; if (cachedCounts.TryGetValue(id, out c) && c >= 0) n += c; else n += s.Builtin != null ? s.Builtin.Length : s.Approx;
            }
            // StevenBlack already contains AdAway, Peter Lowe and Dan Pollock: don't count them twice
            if (cfg.Sources.Contains("stevenblack"))
                foreach (var dup in new[] { "adaway", "yoyo", "pollock" }) if (cfg.Sources.Contains(dup)) { int c; n -= cachedCounts.TryGetValue(dup, out c) && c >= 0 ? c : HostSources.Get(dup).Approx; }
            n += HostsEngine.Lines(cfg.Custom).Count();
            meter.Set(Math.Max(0, n), false);   // an estimate until built: lists overlap
        }

        void Check()
        {
            SyncLists();
            string q = check.Text, r = null;
            AppShell.Current.Busy("Checking " + q + "…", () => { r = HostsEngine.Explain(cfg, q); }, () => { checkResult.Text = r; checkResult.ForeColor = r.Contains("BLOCKED") ? Theme.Accent : Theme.Text; });
        }

        void Run(bool online)
        {
            SyncLists(); SyncOptions();
            if (cfg.Sources.Count == 0 && !HostsEngine.Lines(cfg.Custom).Any()) { Log.W("Pick at least one list (or add your own domains) first."); return; }
            BuildResult r = null;
            AppShell.Current.Busy(online ? "Downloading block lists…" : "Building from saved copies…", () => { r = HostsEngine.Update(cfg, online, null); }, () =>
            {
                loaded = false; Refresh2();
                if (r == null) return;
                meter.Set(r.Total, true);
                if (r.Failed.Count > 0) Dlg.Warn("ArchTweaker skipped some block lists", "ArchTweaker couldn't download them, and it has no saved copy. It applied everything else. Check your connection, then update again.", string.Join("\n", r.Failed.ToArray()));
                if (r.Total > SizeMeter.Heavy) Log.W("Tip: {0:N0} domains is a lot for the Windows hosts file. If lookups feel slow after boot, untick the large lists.", r.Total);
            });
        }

        bool loaded;
        public void OnShow() { Refresh2(); }
        public void Filter(string q) { foreach (var c in cards) c.Visible = q.Length == 0 || (c.S.Name + " " + c.S.Desc + " " + c.S.Group).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0; }

        void Refresh2()
        {
            bool on = HostsModule.IsActive();
            int active = on ? HostsModule.Current().Count : 0;
            state.Text = on ? "● Active: " + active.ToString("N0") + " domains blocked" : "○ Not active";
            state.ForeColor = on ? Theme.Ok : Theme.Muted;
            sub.Text = (cfg.LastUpdate > DateTime.MinValue ? "Last built " + cfg.LastUpdate.ToString("yyyy-MM-dd HH:mm") : "Never built")
                     + "  ·  " + cfg.Sources.Count + " list(s) selected  ·  weekly update " + (cfg.AutoUpdate ? "on" : "off")
                     + "  ·  hosts file: " + (System.IO.File.Exists(HostsModule.File_) ? CleanupModule.Fmt(new System.IO.FileInfo(HostsModule.File_).Length) : "?");
            UpdateMeter();
            if (loaded) return;
            loaded = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var counts = new Dictionary<string, int>();
                foreach (var s in HostSources.All) if (s.Builtin == null) counts[s.Id] = HostsEngine.CachedCount(s.Id);
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        foreach (var kv in counts) cachedCounts[kv.Key] = kv.Value;
                        foreach (var c in cards) { int n; if (cachedCounts.TryGetValue(c.S.Id, out n)) c.SetMeta(n); }
                        UpdateMeter();
                    }));
                }
                catch { }
            });
        }
    }
}
