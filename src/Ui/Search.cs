// ArchTweaker: Global search, tweak filters and filter chips.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ================= FILTER =================
    public static class TweakFilter
    {
        // Saved as an index: new modes go at the end so an older saved value keeps its meaning.
        public static readonly object[] Names = { "Show all tweaks", "Not applied yet", "Medium or high gain", "Not applied, medium+ gain", "High gain only",
            "Applied", "Pending", "Changed outside ArchTweaker", "Needs a restart" };
        // 3.12: the chip strip, in reading order (index into Names, short label)
        public static readonly int[] ChipModes = { 0, 1, 5, 6, 7, 2, 4, 8, 9 };
        public static readonly string[] ChipText = { "All", "Not applied", "Applied", "Pending", "Changed outside", "Medium+ gain", "High gain", "Needs restart", "Proven here" };
        // 3.16: used when the full labels do not fit on one row
        public static readonly string[] ChipShort = { "All", "Not applied", "Applied", "Pending", "Outside", "Medium+", "High only", "Restart", "Proven" };
        static int mode = -1;
        public static int Mode
        {
            get { if (mode < 0) { object o = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "Filter"); mode = o is int && (int)o >= 0 && (int)o < Names.Length ? (int)o : 0; } return mode; }
            set { mode = value; try { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(@"Software\ArchTweaker")) k.SetValue("Filter", value, Microsoft.Win32.RegistryValueKind.DWord); } catch { } }
        }
        // what ArchTweaker last applied (HKCU\...\Applied), cached for a few seconds: a filter pass reads it per row
        static HashSet<string> appliedSet; static DateTime appliedAt;
        static HashSet<string> AppliedIds
        {
            get
            {
                if (appliedSet == null || (DateTime.UtcNow - appliedAt).TotalSeconds > 3)
                { appliedSet = new HashSet<string>(AppliedSet.Read(), StringComparer.OrdinalIgnoreCase); appliedAt = DateTime.UtcNow; }
                return appliedSet;
            }
        }
        // ArchTweaker applied it, and now it reads as off: Windows, a driver or another tool put it back
        public static bool ChangedOutside(Tweak t) { return t.Applicable && StateCache.Get(t) == false && AppliedIds.Contains(t.Id); }
        public static bool Pass(Tweak t, bool on, string q) { return Pass(t, on, q, Mode); }
        public static bool Pass(Tweak t, bool on, string q, int m)
        {
            if ((m == 1 || m == 3) && on) return false;
            if ((m == 2 || m == 3) && t.Impact < 2) return false;
            if (m == 4 && t.Impact < 3) return false;   // 3.2
            if (m == 5 && StateCache.Get(t) != true) return false;
            if (m == 6 && (MainForm.Instance == null || !MainForm.Instance.Pending.ContainsKey(t))) return false;
            if (m == 7 && !ChangedOutside(t)) return false;
            if (m == 8 && !t.Reboot && !t.Relogon) return false;
            if (m == 9) { var pr = ProofStore.Get(t.Id); if (pr == null) return false; string w9; if (!pr.Value.Helped || pr.Value.Stale(out w9)) return false; }   // 3.34
            if (string.IsNullOrEmpty(q)) return true;
            foreach (var w in q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if ((t.Title + " " + t.Desc + " " + t.Gain + " " + t.Category + " " + t.Id + (L.Active ? " " + L.T(t.Title) + " " + L.T(t.Desc) + " " + L.T(t.Gain) + " " + L.T(t.Category) : "")).IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }
        public static bool Shown(Tweak t)
        {
            bool p; if (MainForm.Instance != null && MainForm.Instance.Pending.TryGetValue(t, out p)) return p;
            return t.Applicable && StateCache.Get(t) == true;
        }
    }

    // 3.12: filter chips above a tweak list: one row (wraps when narrow), a count on each chip for the current page.
    // Replaces the "Show" drop-down. Arrow keys move, Enter or Space picks; the name follows the focus for screen readers.
    // 3.33: four chips (All, Not applied, Applied, Pending), "Changed outside" when something was, and "More filters ▾"
    // for gain and restart. The chip of a filter from the menu shows its name and count while it is in use.
    public class FilterChips : Control
    {
        const int MORE = -1;
        static readonly int[] Main = { 0, 1, 2, 3 };       // chip indexes (into TweakFilter.ChipModes)
        const int OUTSIDE = 4;
        static readonly int[] Menu = { 5, 6, 7, 8 };
        readonly List<Rectangle> rects = new List<Rectangle>(); readonly List<int> shown = new List<int>();
        readonly int[] counts = new int[TweakFilter.ChipModes.Length];
        int hover = -1, focus = -1; bool keys;
        public event Action Changed;
        public FilterChips()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true; Font = Theme.FS; BackColor = Theme.Bg; Height = Theme.S(36);
            AccessibleRole = AccessibleRole.PageTabList; AccessibleName = L.T("Show tweaks");
        }
        // category = the page's category; null = every tweak (Home, search)
        public void Recount(string category)
        {
            var list = Catalog.All.Where(t => t.Applicable && Mode.Allows(t) && (category == null || t.Category == category)).ToList();
            for (int i = 0; i < counts.Length; i++) { int m = TweakFilter.ChipModes[i]; counts[i] = list.Count(t => TweakFilter.Pass(t, TweakFilter.Shown(t), null, m)); }
            DoLayout(); Invalidate();
        }
        int Current { get { return Array.IndexOf(TweakFilter.ChipModes, TweakFilter.Mode); } }
        bool MenuActive { get { return Array.IndexOf(Menu, Current) >= 0; } }
        string Label(int k)
        {
            if (k == MORE) return MenuActive ? L.T(TweakFilter.ChipText[Current]) + "  " + counts[Current] + "  ▾" : L.T("More filters") + "  ▾";
            return L.T(TweakFilter.ChipText[k]) + "  " + counts[k];
        }
        void DoLayout()
        {
            shown.Clear(); shown.AddRange(Main);
            if (counts[OUTSIDE] > 0 || Current == OUTSIDE) shown.Add(OUTSIDE);
            shown.Add(MORE);
            rects.Clear();
            int x = 0, y = Theme.S(4), h = Theme.S(26), gap = Theme.S(6);
            foreach (int k in shown)
            {
                int w = TextRenderer.MeasureText(Label(k), Font, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.S(18);
                if (x > 0 && x + w > Width) { x = 0; y += h + gap; }
                rects.Add(new Rectangle(x, y, w, h)); x += w + gap;
            }
            int want = (rects.Count > 0 ? rects[rects.Count - 1].Bottom : h) + Theme.S(8);
            if (Height != want) Height = want;
        }
        protected override void OnResize(EventArgs e) { DoLayout(); base.OnResize(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            int cur = Current;
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i]; int k = shown[i];
                bool on = k == MORE ? MenuActive : k == cur, hot = i == hover;
                bool warn = (k == 3 || k == OUTSIDE) && counts[k] > 0;   // pending / changed outside: worth a look
                Color fill = on ? Theme.Sel : hot ? Theme.PanelHover : Theme.Panel;
                Color edge = on ? Theme.Accent : warn ? Theme.Warn : Theme.Border;
                var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = Gfx.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), r.Height / 2f))
                {
                    using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                    using (var pen = new Pen(edge, on ? Math.Max(1.5f, 1.5f * Theme.Scale) : 1f)) g.DrawPath(pen, p);
                }
                g.SmoothingMode = old;
                bool empty = k != MORE && counts[k] == 0 && k != 0;
                TextRenderer.DrawText(g, Label(k), on ? Theme.Semi(9f) : Font, r, on ? Theme.Strong : empty ? Theme.Muted : Theme.Text, fill, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                if (keys && Focused && i == focus) Gfx.FocusRing(g, RectangleF.Inflate(r, -1, -1), r.Height / 2f);
            }
        }
        int At(Point p) { for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(p)) return i; return -1; }
        protected override void OnMouseMove(MouseEventArgs e) { int h = At(e.Location); if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { int h = At(e.Location); if (h >= 0 && e.Button == MouseButtons.Left) Pick(h); base.OnMouseClick(e); }
        void Pick(int i)
        {
            focus = i;
            if (shown[i] == MORE) { ShowMenu(rects[i]); return; }
            Set(shown[i]);
        }
        void Set(int k)
        {
            TweakFilter.Mode = TweakFilter.ChipModes[k]; DoLayout(); Invalidate(); Announce();
            var c = Changed; if (c != null) c();
        }
        void ShowMenu(Rectangle r)
        {
            var m = new ContextMenuStrip { Renderer = new CarbonMenuRenderer(), Font = Theme.F, BackColor = Theme.Panel, ForeColor = Theme.Text, ShowCheckMargin = true, ShowImageMargin = false };
            foreach (int k in Menu)
            {
                int kk = k;
                var it = new ToolStripMenuItem(L.T(TweakFilter.ChipText[k]) + "  (" + counts[k] + ")") { Checked = Current == k, ForeColor = Theme.Text };
                it.Click += delegate { Set(kk); };
                m.Items.Add(it);
            }
            if (MenuActive)
            {
                m.Items.Add(new ToolStripSeparator());
                var all = new ToolStripMenuItem(L.T("Show all tweaks")) { ForeColor = Theme.Text };
                all.Click += delegate { Set(0); };
                m.Items.Add(all);
            }
            m.Closed += delegate { BeginInvoke((Action)m.Dispose); };
            m.Show(this, new Point(r.Left, r.Bottom + Theme.S(2)));
        }
        protected override void OnGotFocus(EventArgs e) { if (focus < 0) focus = Math.Max(0, shown.IndexOf(Current)); Invalidate(); Announce(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { keys = false; Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int n = rects.Count; if (n == 0) { base.OnKeyDown(e); return; }
            keys = true;
            if (e.KeyCode == Keys.Right) { focus = (focus + 1) % n; e.Handled = true; }
            else if (e.KeyCode == Keys.Left) { focus = (focus - 1 + n) % n; e.Handled = true; }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || e.KeyCode == Keys.Down && focus >= 0 && shown[focus] == MORE) { Pick(Math.Max(0, focus)); e.Handled = true; }
            if (e.Handled) { Invalidate(); Announce(); }
            base.OnKeyDown(e);
        }
        void Announce()
        {
            if (focus < 0 || focus >= shown.Count) return;
            int k = shown[focus];
            AccessibleName = L.T("Show tweaks") + ": " + Label(k) + (k == MORE ? ", " + L.T("opens a menu") : k == Current ? ", " + L.T("selected") : "");
            AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }
    }

    public class SearchPage : FlowLayoutPanel, IPage
    {
        readonly Dictionary<Tweak, TweakRow> cache = new Dictionary<Tweak, TweakRow>();
        Label head; string q = "";
        public SearchPage()
        {
            FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoScroll = true; BackColor = Theme.Bg; Theme.VerticalOnly(this);
            head = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 8), UseMnemonic = false };
            Controls.Add(head);
            Resize += delegate { foreach (Control c in Controls) if (c is TweakRow) c.Width = Math.Max(300, ClientSize.Width - Padding.Horizontal - 6); };
        }
        public void OnShow() { }
        public void Filter(string s) { Query(s); }
        public void Reload() { if (q.Length > 0) Query(q); }

        public void Query(string query)
        {
            q = query;
            SuspendLayout();
            foreach (Control c in Controls.Cast<Control>().ToList()) if (c != head) { Controls.Remove(c); if (!(c is TweakRow)) c.Dispose(); }   // rows are cached; per-query labels are not
            int n = 0; string lastCat = null;
            foreach (var t in Catalog.All)
            {
                if (!Mode.Allows(t)) continue;   // 3.22
                bool p; bool pend = MainForm.Instance.Pending.TryGetValue(t, out p);
                bool? known = t.Applicable ? StateCache.Get(t) : (bool?)false;
                bool applied = known ?? false;
                if (!TweakFilter.Pass(t, pend ? p : applied, q)) continue;
                if (t.Category != lastCat)
                {
                    lastCat = t.Category;
                    var cl = new LinkLabel { Text = t.Category, AutoSize = true, Font = Theme.FB, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Strong, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, n == 0 ? 0 : 10, 0, 6), UseMnemonic = false };
                    string cat = t.Category;
                    cl.LinkClicked += delegate { MainForm.Instance.ShowPage(cat); };
                    Controls.Add(cl);
                }
                TweakRow r;
                if (!cache.TryGetValue(t, out r))
                {
                    r = new TweakRow(t);
                    if (!t.Applicable) r.MarkUnavailable(t.UnavailableReason);
                    cache[t] = r;
                }
                r.Width = Math.Max(300, Math.Min(Theme.S(Theme.MaxContent), ClientSize.Width - Padding.Horizontal - 6));
                r.Load(known, pend ? (bool?)p : null); r.SetHighlight(q);
                Controls.Add(r); n++;
            }
            head.Text = n == 0
                ? "No tweak matches \"" + q + "\"" + (TweakFilter.Mode != 0 ? " with the filter '" + TweakFilter.Names[TweakFilter.Mode] + "'. Click All above the list to widen the search." : ". Try a shorter word, like \"ping\", \"usb\" or \"timer\".")
                : n + " tweak" + (n == 1 ? "" : "s") + " match \"" + q + "\" across all categories. Esc clears the search.";
            ResumeLayout();
            // filters on applied state need real states: read the missing ones, then run the query again
            var missing = Catalog.All.Where(t => t.Applicable && !StateCache.Get(t).HasValue).ToList();
            if (missing.Count > 0) { string again = q; StateCache.ReadAsync(missing, this, () => { if (q == again) Query(again); }); }
        }
    }
}
