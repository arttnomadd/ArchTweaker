// ArchTweaker: Sidebar items and section headers, with the page glyphs.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArchTweaker
{
    // Sidebar entry: name on the left, applied count on the right, red edge when active.
    public class NavItem : Control
    {
        public string Page; public string Count = ""; bool active, hover;
        static readonly ToolTip tips = Tips.New(new ToolTip { InitialDelay = 500 });
        // 2.8: short sidebar labels for long page names (the page title keeps the full name)
        static readonly Dictionary<string, string> Short = new Dictionary<string, string> { { Cat.Explorer, "Explorer & menus" }, };
        // Segoe Fluent Icons (Win 11) / Segoe MDL2 Assets (Win 10) code points, one per page
        // 3.16: the page header shows the same glyph as the sidebar
        public static bool TryGlyph(string page, out char c) { return Glyph.TryGetValue(page, out c); }
        static readonly Dictionary<string, char> Glyph = new Dictionary<string, char>
        {
            { "Home", '\uE80F' }, { Cat.Privacy, '\uE72E' }, { Cat.Interface, '\uE771' }, { Cat.Explorer, '\uE8B7' }, { Cat.System, '\uE713' },
            { Cat.Responsiveness, '\uE945' }, { Cat.Gaming, '\uE7FC' }, { Cat.PowerPlan, '\uE7E8' }, { Cat.Input, '\uE962' }, { Cat.Gpu, '\uE7F4' },
            { Cat.Network, '\uE774' }, { Cat.TasksServices, '\uE912' }, { Cat.Commands, '\uE756' }, { Catalog.HARDENING, '\uEA18' },
            { DangerZone.CAT, '\uE7BA' },
            { "PC advisor", '\uE82F' }, { "System scan", '\uE9D9' }, { "Services", '\uE912' }, { "Apps (UWP)", '\uE71D' }, { "Startup", '\uE768' },
            { "Cleanup", '\uE74D' }, { "Deep clean", '\uE74D' }, { "Uninstaller", '\uE738' }, { "Power settings", '\uE9E9' }, { "Context menu", '\uE8FD' },
            { "Hosts blocker", '\uE8F8' }, { "Measure", '\uE9D2' }, { "A/B test", '\uE8EF' }, { "Tools & backup", '\uE90F' }, { "Settings", '\uE713' }, { "System info", '\uE946' },
            { "Latency", '\uEC4A' }, { "Benchmark", '\uE9D9' }, { "Drivers", '\uE964' }, { "What changed", '\uE81C' }, { "Change history", '\uE787' }, { "Disk space", '\uEDA2' },
            { "Tuning assistant", '\uE9D5' }, { "Live overlay", '\uE7B3' }, { "Interrupt tuner", '\uE950' }, { "Power tuner", '\uE945' }, { "Tune everything", '\uE768' }, { "Results", '\uE9D9' }, { "Mouse polling", '\uE962' }, { "Hardware audit", '\uE9F9' }, { "Boot times", '\uE7E8' }, { "Bake into ISO", '\uE958' },   // 3.19
        };
        static Font iconFont; static bool iconChecked;
        public static Font IconFont
        {
            get
            {
                if (!iconChecked)
                {
                    iconChecked = true;
                    foreach (var f in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
                        if (FontFamily.Families.Any(x => x.Name == f)) { iconFont = new Font(f, 11f * Theme.Dpi / 96f, GraphicsUnit.Pixel); break; }
                }
                return iconFont;
            }
        }
        public NavItem(string page)
        {
            Page = page; string label; Text = Short.TryGetValue(page, out label) ? label : page;
            tips.SetToolTip(this, L.T(page));
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(196, Theme.Slim ? 34 : 30); Margin = new Padding(0, 1, 0, 1); Cursor = Cursors.Hand; TabStop = true; Font = Theme.F;
            AccessibleRole = AccessibleRole.PageTab; AccessibleName = page;
            if (page == DangerZone.CAT) Margin = new Padding(0, Theme.S(10), 0, 1);   // 3.4: set apart from the ordinary pages
        }
        Color GlyphColor { get { return Page == DangerZone.CAT ? Theme.Risk : active ? Theme.Accent : Theme.Muted; } }
        public bool Active { get { return active; } set { if (active == value) return; active = value; Invalidate(); AccessibilityNotifyClients(AccessibleEvents.SelectionAdd, -1); } }
        public void SetCount(string c)
        {
            Count = c; Invalidate();
            int a, b; AccessibleDescription = Parse(c, out a, out b) ? a + " of " + b + " applied" : c;
            // 3.33: the slim sidebar shows only icons, so the tooltip carries the count
            tips.SetToolTip(this, L.T(Page) + (Parse(c, out a, out b) ? "  ·  " + L.F("{0} of {1} applied", a, b) : ""));
            tips.SetToolTip(this, L.T(Page + (Parse(c, out a, out b) ? "  ·  " + a + " of " + b + " applied" : "")));
        }
        static bool Parse(string c, out int a, out int b)
        {
            a = b = 0; var p = (c ?? "").Split('/');
            return p.Length == 2 && int.TryParse(p[0], out a) && int.TryParse(p[1], out b) && b > 0;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            else if ((e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) && Parent != null) { Parent.SelectNextControl(this, e.KeyCode == Keys.Down, true, false, true); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            // 3.36: the active and hovered entries are inset pills; the active one carries a short accent bar inside it
            Color bg = active ? Theme.PendingBg : hover ? Theme.PanelHover : Theme.Side;
            g.Clear(Theme.Side);
            if (active || hover)
            {
                var o = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = Gfx.Round(new RectangleF(Theme.S(4f), 0.5f, Width - Theme.S(8f), Height - 1.5f), Theme.S((float)Theme.Radius))) using (var b = new SolidBrush(bg)) g.FillPath(b, p);
                if (active) using (var p = Gfx.Round(new RectangleF(Theme.S(4f), Theme.S(8f), Theme.S(3f), Height - Theme.S(16f)), Theme.S(1.5f))) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, p);
                g.SmoothingMode = o;
            }
            int x = Theme.S(14);
            char gl; var icf = IconFont;
            if (Theme.Slim) { PaintSlim(g, icf, bg); return; }
            if (icf != null && Glyph.TryGetValue(Page, out gl))
            {
                TextRenderer.DrawText(g, gl.ToString(), icf, new Rectangle(x, 0, Theme.S(20), Height), GlyphColor, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                x += Theme.S(28);
            }
            int a, n; bool hasCount = Parse(Count, out a, out n);
            int pillW = hasCount ? PillWidth() : 0;
            int pendW = Pend > 0 ? PendWidth(Pend) + Theme.S(4) : 0;
            var tr = new Rectangle(x, 0, Width - x - pillW - pendW - Theme.S(12), Height);
            TextRenderer.DrawText(g, L.T(Text), active ? Theme.BodyBold(9.5f) : Font, tr, active ? Theme.Strong : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (hasCount) DrawPill(g, new Rectangle(Width - pillW - Theme.S(8), (Height - Theme.S(18)) / 2, pillW, Theme.S(18)), a, n, bg);
            if (Pend > 0) DrawPend(g, new Rectangle(Width - pillW - Theme.S(8) - pendW, (Height - Theme.S(18)) / 2, pendW - Theme.S(4), Theme.S(18)), Pend);
            if (Focused && ShowFocusCues) Gfx.FocusRing(g, new RectangleF(1, 1, Width - 3, Height - 3), Theme.S(Theme.Radius));
        }
        // 3.2: slim sidebar: the page icon centred, progress as a thin bar under it (full = green), name in the tooltip
        void PaintSlim(Graphics g, Font icf, Color bg)
        {
            char gl;
            Color fg = Page == DangerZone.CAT ? Theme.Risk : active ? Theme.Accent : Theme.Text;
            if (icf != null && Glyph.TryGetValue(Page, out gl))
                TextRenderer.DrawText(g, gl.ToString(), icf, new Rectangle(0, -Theme.S(2), Width, Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            else
            {   // no icon font (very old Windows): two letters of the page name
                string t = L.T(Text); string ab = t.Length >= 2 ? t.Substring(0, 2) : t;
                TextRenderer.DrawText(g, ab, active ? Theme.BodyBold(9.5f) : Font, new Rectangle(0, -Theme.S(2), Width, Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            }
            int a, n;
            if (Pend > 0) using (var b = new SolidBrush(Theme.Warn)) g.FillEllipse(b, Width - Theme.S(18), Theme.S(4), Theme.S(8), Theme.S(8));   // 3.12: pending dot
            if (Parse(Count, out a, out n) && a > 0)
            {
                int w = Theme.S(22), x0 = (Width - w) / 2, y = Height - Theme.S(5), h = Math.Max(2, Theme.S(2));
                using (var b = new SolidBrush(Gfx.Mix(bg, Theme.Text, 0.22))) g.FillRectangle(b, x0, y, w, h);
                using (var b = new SolidBrush(Theme.Progress)) g.FillRectangle(b, x0, y, Math.Max(h, (int)Math.Round(w * (double)Math.Min(a, n) / n)), h);
            }
            if (Focused && ShowFocusCues) Gfx.FocusRing(g, new RectangleF(1, 1, Width - 3, Height - 3), Theme.S(Theme.Radius));
        }
        // 3.12: changes on this page that wait for Apply: an amber "+N" badge left of the applied count
        public int Pend;
        public void SetPend(int n) { if (n == Pend) return; Pend = n; Invalidate(); AccessibilityNotifyClients(AccessibleEvents.NameChange, -1); }
        public static int PendWidth(int n) { return Math.Max(Theme.S(24), TextRenderer.MeasureText("+" + n, Theme.FT, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.S(10)); }
        public static void DrawPend(Graphics g, Rectangle r, int n)
        {
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Gfx.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), r.Height / 2f)) using (var b = new SolidBrush(Theme.Warn)) g.FillPath(b, p);
            g.SmoothingMode = old;
            Color ink = AccentMaker.Contrast(Color.White, Theme.Warn) >= 4.5 ? Color.White : Color.FromArgb(22, 22, 22);
            TextRenderer.DrawText(g, "+" + n, Theme.FT, r, ink, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }
        int PillWidth() { return Math.Max(Theme.S(36), TextRenderer.MeasureText(Count, Theme.FT, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.S(12)); }
        // applied/total: neutral pill, a 2 px accent bar for partial progress, solid accent when the whole page is applied
        void DrawPill(Graphics g, Rectangle r, int a, int n, Color under)
        {
            bool full = a >= n, none = a == 0;
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Gfx.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), r.Height / 2f))
            {
                using (var b = new SolidBrush(full ? Theme.On : Gfx.Mix(under, Theme.Text, 0.08))) g.FillPath(b, p);
                if (!full && !none)
                {   // progress track under the numbers: muted full width, accent for the applied share
                    int inset = Theme.S(6), y = r.Bottom - Theme.S(4), w = r.Width - inset * 2, h = Math.Max(2, Theme.S(2));
                    using (var b = new SolidBrush(Gfx.Mix(under, Theme.Text, 0.22))) g.FillRectangle(b, r.X + inset, y, w, h);
                    using (var b = new SolidBrush(Theme.Progress)) g.FillRectangle(b, r.X + inset, y, Math.Max(h, (int)Math.Round(w * (double)a / n)), h);
                }
            }
            g.SmoothingMode = old;
            var tr = full || none ? r : new Rectangle(r.X, r.Y - Theme.S(2), r.Width, r.Height);   // lift the text above the track
            TextRenderer.DrawText(g, Count, Theme.FT, tr, full ? Theme.OnInk : none ? Theme.Muted : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }
        // Up/Down walk the sidebar like a list; Enter/Space opens the page
        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }
        protected override void OnPreviewKeyDown(PreviewKeyDownEventArgs e) { if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) e.IsInputKey = true; base.OnPreviewKeyDown(e); }
        protected override AccessibleObject CreateAccessibilityInstance() { return new NavAcc(this); }
        class NavAcc : ControlAccessibleObject
        {
            readonly NavItem n; public NavAcc(NavItem n) : base(n) { this.n = n; }
            public override AccessibleRole Role { get { return AccessibleRole.PageTab; } }
            public override string Name { get { string b = L.T(n.Page); return n.Pend > 0 ? b + ", " + n.Pend + " " + L.T("pending") : b; } }
            public override AccessibleStates State { get { var s = base.State | AccessibleStates.Selectable | AccessibleStates.Focusable; if (n.Active) s |= AccessibleStates.Selected; return s; } }
            public override string DefaultAction { get { return "Open"; } }
            public override void DoDefaultAction() { n.PerformOpen(); }
        }
        public void PerformOpen() { OnClick(EventArgs.Empty); }
    }

    // Collapsible sidebar section header.
    public class NavHeader : Control
    {

        public bool Open = true; bool hover;
        public int Pend;   // 3.12: pending changes in the section; shown while it is collapsed
        public void SetPend(int n) { if (n == Pend) return; Pend = n; Invalidate(); }
        public NavHeader(string text)
        {
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            Size = new Size(196, Theme.Slim ? 16 : 26); Margin = new Padding(0, Theme.Slim ? 6 : 8, 0, 2); Cursor = Cursors.Hand; TabStop = true;
            AccessibleRole = AccessibleRole.OutlineButton; AccessibleName = text;
            if (Theme.Slim) { var tip = Tips.New(new ToolTip { InitialDelay = 400 }); tip.SetToolTip(this, L.T(text)); Disposed += delegate { tip.Dispose(); }; }
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Theme.Side)) g.FillRectangle(b, ClientRectangle);
            Color c = hover ? Theme.Strong : Theme.Muted;   // 3.1: section headers are structure, not a call to action
            if (Theme.Slim)
            {   // 3.2: slim sidebar: a divider with the open/closed arrow; the section name is in the tooltip
                float mx = Width / 2f, my = Height / 2f, uu = Theme.Scale;
                using (var pen = new Pen(Theme.Border)) { g.DrawLine(pen, Theme.S(4), my, mx - 8 * uu, my); g.DrawLine(pen, mx + 8 * uu, my, Width - Theme.S(4), my); }
                var tri = Open ? new[] { new PointF(mx - 4 * uu, my - 2 * uu), new PointF(mx + 4 * uu, my - 2 * uu), new PointF(mx, my + 3 * uu) } : new[] { new PointF(mx - 2 * uu, my - 4 * uu), new PointF(mx - 2 * uu, my + 4 * uu), new PointF(mx + 3 * uu, my) };
                using (var b = new SolidBrush(c)) g.FillPolygon(b, tri);
                if (Focused && ShowFocusCues) Gfx.FocusRing(g, new RectangleF(1, 1, Width - 3, Height - 3), Theme.S(Theme.Radius));
                return;
            }
            float cx = Theme.S(12f), cy = Height / 2f, u = Theme.Scale;
            var pts = Open ? new[] { new PointF(cx - 4 * u, cy - 2 * u), new PointF(cx + 4 * u, cy - 2 * u), new PointF(cx, cy + 3 * u) } : new[] { new PointF(cx - 2 * u, cy - 4 * u), new PointF(cx - 2 * u, cy + 4 * u), new PointF(cx + 3 * u, cy) };
            using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
            TextRenderer.DrawText(g, Theme.Cap(Text), Theme.FT, new Rectangle(Theme.S(22), 0, Width - Theme.S(22), Height), c, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            if (!Open && Pend > 0) { int pw = NavItem.PendWidth(Pend); NavItem.DrawPend(g, new Rectangle(Width - pw - Theme.S(8), (Height - Theme.S(18)) / 2, pw, Theme.S(18)), Pend); }
            if (Focused && ShowFocusCues) Gfx.FocusRing(g, new RectangleF(1, 1, Width - 3, Height - 3), Theme.S(Theme.Radius));
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new HeadAcc(this); }
        class HeadAcc : ControlAccessibleObject
        {
            readonly NavHeader h; public HeadAcc(NavHeader h) : base(h) { this.h = h; }
            public override AccessibleRole Role { get { return AccessibleRole.OutlineButton; } }
            public override AccessibleStates State { get { return base.State | AccessibleStates.Focusable | (h.Open ? AccessibleStates.Expanded : AccessibleStates.Collapsed); } }
            public override string DefaultAction { get { return h.Open ? "Collapse" : "Expand"; } }
        }
    }
}
