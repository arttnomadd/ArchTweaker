// ArchTweaker: Window shell extras: command palette (Ctrl+K), health strip and tray icon.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ===================== COMMAND PALETTE (Ctrl+K) =====================
    public class CommandPalette : Form
    {
        public class Cmd { public string Kind, Text, Search; public Action Run; }
        readonly TextBox box = new TextBox();
        readonly ListBox list = new ListBox();
        readonly List<Cmd> all;
        List<Cmd> shown = new List<Cmd>();
        Cmd chosen;

        public static void Open(Form owner, List<Cmd> cmds)
        {
            Cmd pick;
            using (var p = new CommandPalette(cmds))
            {
                p.Location = new Point(owner.Left + (owner.Width - p.Width) / 2, owner.Top + Theme.S(90));
                p.ShowDialog(owner); pick = p.chosen;
            }
            if (pick != null) { try { pick.Run(); } catch (Exception ex) { Log.W("Command '{0}': {1}", pick.Text, ex.Message); } }
        }
        CommandPalette(List<Cmd> cmds)
        {
            all = cmds;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; KeyPreview = true;
            BackColor = Theme.Border; Padding = new Padding(1); Size = new Size(Theme.S(620), Theme.S(420)); AutoScaleMode = AutoScaleMode.None;
            AccessibleName = L.T("Command palette"); Text = L.T("Command palette"); Chrome.Hook(this);   // borderless: only the small rounded corners apply; 3.39: Text names the window for UI Automation
            var inner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(Theme.S(12)) };
            box.Dock = DockStyle.Top; box.BorderStyle = BorderStyle.None; box.Font = Theme.Semi(12f); box.BackColor = Theme.Panel; box.ForeColor = Theme.Text; box.AccessibleName = L.T("Type a page, tweak or action");
            var hint = new Label { Dock = DockStyle.Top, Height = Theme.S(24), Text = L.T("Type a page, tweak or action. Enter opens it, Esc closes."), ForeColor = Theme.Muted, Font = Theme.FS, UseMnemonic = false, TextAlign = ContentAlignment.BottomLeft };
            list.Dock = DockStyle.Fill; list.BorderStyle = BorderStyle.None; list.BackColor = Theme.Panel; list.ForeColor = Theme.Text; list.Font = Theme.F;
            list.DrawMode = DrawMode.OwnerDrawFixed; list.ItemHeight = Theme.S(30); list.IntegralHeight = false; list.AccessibleName = L.T("Results");
            list.DrawItem += DrawItem;
            list.DoubleClick += delegate { Pick(); };
            box.TextChanged += delegate { Refilter(); };
            inner.Controls.Add(list); inner.Controls.Add(hint); inner.Controls.Add(box);
            Controls.Add(inner);
            Deactivate += delegate { if (Visible) Close(); };
            Refilter();
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys k)
        {
            if (k == Keys.Escape) { Close(); return true; }
            if (k == Keys.Enter) { Pick(); return true; }
            if (k == Keys.Down) { if (list.Items.Count > 0) list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1); return true; }
            if (k == Keys.Up) { if (list.Items.Count > 0) list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); return true; }
            return base.ProcessCmdKey(ref msg, k);
        }
        void Pick() { if (list.SelectedIndex >= 0 && list.SelectedIndex < shown.Count) { chosen = shown[list.SelectedIndex]; Close(); } }
        void Refilter()
        {
            var words = box.Text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            shown = all.Where(c => words.All(w => c.Search.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                       .OrderBy(c => c.Text.Equals(box.Text.Trim(), StringComparison.OrdinalIgnoreCase) ? 0 : 1)   // 3.39: an exact name always wins
                       .ThenBy(c => words.Length > 0 && c.Text.StartsWith(words[0], StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                       .ThenBy(c => c.Kind == "Page" ? 0 : c.Kind == "Action" ? 1 : 2).Take(60).ToList();
            list.BeginUpdate(); list.Items.Clear(); foreach (var c in shown) list.Items.Add(c.Text); list.EndUpdate();
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }
        void DrawItem(object s, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= shown.Count) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.Sel : Theme.Panel)) e.Graphics.FillRectangle(b, e.Bounds);
            var c = shown[e.Index];
            var r = new Rectangle(e.Bounds.X + Theme.S(8), e.Bounds.Y, e.Bounds.Width - Theme.S(130), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, c.Text, Theme.F, r, sel ? Theme.Strong : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, L.T(c.Kind), Theme.FS, new Rectangle(e.Bounds.Right - Theme.S(120), e.Bounds.Y, Theme.S(110), e.Bounds.Height), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
    }

    // ===================== HOME: HEALTH STRIP =====================
    public class HealthStrip : Control
    {
        HealthInfo h; readonly List<Tuple<Rectangle, Action>> hits = new List<Tuple<Rectangle, Action>>();
        int hot = -1;
        public HealthStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(150); AccessibleRole = AccessibleRole.Grouping; AccessibleName = "PC health";
        }
        // 3.36: the page's one authored motion: the score counts up and the scale fills, ≈600 ms ease-out,
        // from the last value shown (0 the first time). Off when Windows animations are off.
        double shown = -1;
        public void Set(HealthInfo info)
        {
            h = info;
            if (info != null)
            {
                double from = shown < 0 ? 0 : shown, to = info.Score;
                if (!Motion.On || !IsHandleCreated || Math.Abs(to - from) < 0.5) shown = to;
                else
                {
                    double t = 0;
                    Motion.Run(this, dt => { t = Math.Min(1, t + dt / 600.0); double k = 1 - Math.Pow(1 - t, 4); shown = from + (to - from) * k; Invalidate(); return t >= 1; });
                }
            }
            Invalidate();
            AccessibleDescription = info == null ? "" : L.T("Health score") + " " + info.Score + "/100";
        }
        protected override void OnMouseMove(MouseEventArgs e) { int i = hits.FindIndex(x => x.Item1.Contains(e.Location)); if (i != hot) { hot = i; Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { var t = hits.FirstOrDefault(x => x.Item1.Contains(e.Location)); if (t != null) t.Item2(); base.OnMouseClick(e); }
        // 3.36: one instrument panel. Left: the score as a big read-out over a 20-cell scale, what it counts, and the main
        // action as the page's one red button. Right, behind a hairline: two read-outs that go somewhere.
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent != null ? Parent.BackColor : Theme.Bg); hits.Clear();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int sh = Theme.S(6);
            var card = new RectangleF(0, 0, Width - 1, Height - sh);
            Gfx.Surface(g, card, Theme.Raised, true);
            int pad = Theme.S(20);
            int leftW = (int)(card.Width * 0.54f);
            int colW = ((int)card.Width - leftW) / 2;

            // ---- right: two read-outs (hover lights the column; the whole column is the button)
            int[] side = { 2, 4 };
            for (int n = 0; n < side.Length; n++)
            {
                string title, value, action; Color tone; Action go;
                Tile(side[n], out title, out value, out action, out tone, out go);
                var col = new Rectangle(leftW + n * colW, 0, n == side.Length - 1 ? (int)card.Width - leftW - colW : colW, (int)card.Height);
                bool isHot = go != null && hits.Count == hot;
                if (isHot)
                {
                    var hr = new RectangleF(col.X + 1, col.Y + 1, col.Width - 2, col.Height - 2);
                    if (n == side.Length - 1) using (var p = Gfx.Round(hr, Theme.S((float)Theme.Radius))) using (var b = new SolidBrush(Theme.PanelHover)) g.FillPath(b, p);
                    else using (var b = new SolidBrush(Theme.PanelHover)) g.FillRectangle(b, hr);
                }
                Gfx.Hair(g, col.X, col.Y + Theme.S(16), col.X, col.Bottom - Theme.S(16));
                Color fill = isHot ? Theme.PanelHover : Theme.Raised;
                int x = col.X + pad;
                TextRenderer.DrawText(g, Theme.Cap(title), Theme.FT, new Rectangle(x, Theme.S(18), col.Width - pad * 2, Theme.S(16)), Theme.Muted, fill, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, L.T(value), Theme.Num(20f), new Rectangle(x - Theme.S(2), Theme.S(40), col.Width - pad * 2, Theme.S(40)), tone, fill, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                if (action != null && go != null)
                {
                    var af = isHot ? LinkFont : Theme.FB;
                    TextRenderer.DrawText(g, L.T(action) + "  \u203A", af, new Rectangle(x, (int)card.Height - Theme.S(38), col.Width - pad * 2, Theme.S(20)), isHot ? Theme.Strong : Theme.Text, fill, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    hits.Add(Tuple.Create(col, go));
                }
            }

            // ---- left: the score
            {
                string title, value, action; Color tone; Action go;
                Tile(0, out title, out value, out action, out tone, out go);
                Color fill = Theme.Raised;
                TextRenderer.DrawText(g, Theme.Cap(title), Theme.FT, new Rectangle(pad, Theme.S(18), leftW - pad * 2, Theme.S(16)), Theme.Muted, fill, TextFormatFlags.NoPrefix);
                double now = h == null ? 0 : (shown < 0 ? h.Score : shown);
                string score = h == null ? "…" : ((int)Math.Round(now)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var big = Theme.Num(38f); var small = Theme.Num(15f);
                var bs = TextRenderer.MeasureText(g, score, big, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                int ny = Theme.S(34);
                TextRenderer.DrawText(g, score, big, new Point(pad - Theme.S(2), ny), h != null && h.Score >= 80 ? Theme.Ok : Theme.Strong, fill, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                if (h != null)
                {
                    var ss = TextRenderer.MeasureText(g, "/100", small, Size.Empty, TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, "/100", small, new Point(pad + bs.Width + Theme.S(4), ny + bs.Height - ss.Height - Theme.S(5)), Theme.Muted, fill, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
                // the main action: the page's one filled red button, right of the number
                int btnW = 0;
                if (action != null && go != null)
                {
                    var bf = Theme.FB; string bt = L.T(action);
                    btnW = TextRenderer.MeasureText(g, bt, bf, Size.Empty, TextFormatFlags.NoPrefix).Width + Theme.S(36);
                    int bh = Theme.S(34);
                    var br = new Rectangle(leftW - pad - btnW, ny + Theme.S(8), btnW, bh);
                    bool isHot = hits.Count == hot;
                    Color bc = isHot ? Theme.AccentDark : Theme.Accent;
                    using (var p = Gfx.Round(br, Theme.S((float)Theme.Radius))) using (var b = new SolidBrush(bc)) g.FillPath(b, p);
                    using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawLine(pen, br.X + Theme.S(4), br.Y + 0.5f, br.Right - Theme.S(4), br.Y + 0.5f);
                    TextRenderer.DrawText(g, bt, bf, br, Theme.OnAccent, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                    hits.Add(Tuple.Create(br, go));
                }
                // the 20-cell scale and what the score counts
                var scale = new RectangleF(pad, card.Height - Theme.S(44), leftW - pad * 2, Math.Max(4, Theme.S(6)));
                Color on = h != null && h.Score >= 80 ? Theme.Ok : Theme.Strong;
                Gfx.Segments(g, scale, 20, now / 100.0, on, Gfx.Mix(fill, Theme.Text, 0.12));
                if (h != null)
                    TextRenderer.DrawText(g, L.F("{0} of {1} speed tweaks applied", h.SpeedApplied, h.SpeedTotal), Theme.FS, new Rectangle(pad, (int)scale.Bottom + Theme.S(8), leftW - pad * 2, Theme.S(18)), Theme.Muted, fill, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
        static Font linkFont;
        static Font LinkFont
        {
            get
            {
                var fb = Theme.FB;
                if (linkFont == null || linkFont.Size != fb.Size || linkFont.Name != fb.Name)
                {
                    var old = linkFont;
                    linkFont = new Font(fb, fb.Style | FontStyle.Underline);
                    if (old != null) old.Dispose();   // one font per text size / DPI, not one more per change
                }
                return linkFont;
            }
        }
        void Tile(int i, out string title, out string value, out string action, out Color tone, out Action go)
        {
            tone = Theme.Strong; action = null; go = null;
            string[] titles = { "Health score", "Speed tweaks", "Reset by Windows", "Restart", "Last cleanup" };
            title = titles[i];
            if (h == null) { value = "…"; return; }
            switch (i)
            {
                case 0:
                    value = h.Score + " / 100";
                    tone = h.Score >= 80 ? Theme.Ok : Theme.Strong;   // 3.1: an untuned PC isn't an error
                    action = Mode.Lite ? "Optimize safely" : "Select the picks for this PC"; go = () => { if (Mode.Lite) MainForm.Instance.OptimizeSafely(); else MainForm.Instance.SelectAutoPreset(); }; break;
                case 1:
                    value = h.SpeedApplied + " / " + h.SpeedTotal;
                    action = "Open PC advisor"; go = () => MainForm.Instance.ShowPage("PC advisor"); break;
                case 2:
                    value = h.Drifted == 0 ? "None" : h.Drifted.ToString();
                    tone = h.Drifted == 0 ? Theme.Ok : Theme.Warn;
                    action = h.Drifted == 0 ? "Check what changed" : "Fix them"; go = () => MainForm.Instance.ShowPage("What changed"); break;
                case 3:
                    value = h.RestartPending ? "Needed" : "Not needed"; tone = h.RestartPending ? Theme.Warn : Theme.Ok;
                    if (h.RestartPending) { action = "Restart now"; go = () => MainForm.Instance.RestartNowPublic(); }
                    break;
                default:
                    double days = (DateTime.Now - h.LastClean).TotalDays;
                    value = h.LastClean == DateTime.MinValue ? "Never" : days < 1 ? "Today" : (int)days + (((int)days) == 1 ? " day ago" : " days ago");
                    tone = days <= 14 ? Theme.Ok : Theme.Warn;
                    action = "Clean up"; go = () => MainForm.Instance.ShowPage("Cleanup"); break;
            }
        }
    }

    // ===================== TRAY ICON =====================
    public static class Tray
    {
        const string KEY = @"Software\ArchTweaker";
        public static bool Enabled
        {
            get { object o = Reg.Get(Hive.HKCU, KEY, "Tray"); return o == null || o.ToString() != "0"; }
            set { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(KEY)) k.SetValue("Tray", value ? "1" : "0"); }
        }
        public static bool CloseToTray
        {
            get { object o = Reg.Get(Hive.HKCU, KEY, "CloseToTray"); return o != null && o.ToString() == "1"; }
            set { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(KEY)) k.SetValue("CloseToTray", value ? "1" : "0"); }
        }

        public static NotifyIcon Create(MainForm f)
        {
            var ni = new NotifyIcon { Text = "ArchTweaker " + Program.Version };
            try { ni.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { ni.Icon = SystemIcons.Application; }
            var menu = new ContextMenuStrip { Renderer = new CarbonMenuRenderer(), Font = Theme.F, BackColor = Theme.Panel, ForeColor = Theme.Text };
            var open = new ToolStripMenuItem("Open ArchTweaker") { Font = Theme.FB };
            open.Click += delegate { f.ShowFromTray(); };
            var plans = new ToolStripMenuItem("Power plan");
            plans.DropDownItems.Add(new ToolStripMenuItem("…"));
            plans.DropDownOpening += delegate
            {
                plans.DropDownItems.Clear();
                foreach (var p in PowerPlans.List())
                {
                    var pp = p; var it = new ToolStripMenuItem(p.Name) { Checked = p.Active };
                    it.Click += delegate { PowerPlans.Activate(pp.Guid); ni.ShowBalloonTip(2500, "ArchTweaker", L.T("Power plan:") + " " + pp.Name, ToolTipIcon.Info); };
                    plans.DropDownItems.Add(it);
                }
            };
            var reassert = new ToolStripMenuItem("Re-assert active profile");
            reassert.Click += delegate
            {
                string ap = HealthReport.ActiveProfile();
                if (string.IsNullOrEmpty(ap) || !File.Exists(ap)) { ni.ShowBalloonTip(3000, "ArchTweaker", L.T("No active profile set. Set one in Tools & backup → Maintenance."), ToolTipIcon.Warning); return; }
                int n = 0;
                f.Busy("Re-asserting profile…", delegate { n = Drift.Reassert(Drift.Against(Profile.Read(ap).ToList())); }, delegate { ni.ShowBalloonTip(3000, "ArchTweaker", L.T("Re-asserted " + n + (n == 1 ? " tweak." : " tweaks.")), ToolTipIcon.Info); });
            };
            var clean = new ToolStripMenuItem("Clean temp files");
            clean.Click += delegate
            {
                long freed = 0;
                f.Busy("Cleaning temp files…", delegate
                {
                    foreach (var t in CleanupModule.Targets().Where(x => x.Title == "User temp files" || x.Title == "Windows temp files")) { int sk; freed += CleanupModule.Clean(t, out sk); }
                    CleanSettings.MarkCleaned(freed);
                }, delegate { ni.ShowBalloonTip(3000, "ArchTweaker", L.T("Freed " + CleanupModule.Fmt(freed) + "."), ToolTipIcon.Info); });
            };
            var exit = new ToolStripMenuItem("Exit");
            exit.Click += delegate { f.ExitFromTray(); };
            menu.Items.Add(open); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(plans); menu.Items.Add(reassert); menu.Items.Add(clean);
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(exit);
            L.Apply(menu.Items);
            ni.ContextMenuStrip = menu;
            ni.DoubleClick += delegate { f.ShowFromTray(); };
            ni.Visible = true;
            return ni;
        }
    }
}
