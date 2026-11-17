// ArchTweaker: Owner-drawn check list (Cleanup, Startup).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArchTweaker
{
    // Selectable list row for the Cleanup and Deep clean pages. The whole row is the hit target:
    // a 20px Carbon check box, title, one muted (or warning) detail line and a right-aligned size.
    // Checked rows get an accent edge so the selection reads at a glance down a long list.
    public class CheckRow : Control
    {
        bool on, hover;
        string detail, size = "—";
        public bool Warning;
        public event EventHandler CheckedChanged;
        public CheckRow(string title, string detail, bool warning, bool on)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Text = title; this.detail = detail; Warning = warning; this.on = on;
            Height = Theme.S(string.IsNullOrEmpty(detail) ? 44 : 58);
            Cursor = Cursors.Hand; TabStop = true; BackColor = Theme.Bg;
            AccessibleRole = AccessibleRole.CheckButton; AccessibleName = title;
            AccessibleDescription = detail;
        }
        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); AccessibilityNotifyClients(AccessibleEvents.StateChange, -1); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); }
        }
        public string SizeText { get { return size; } set { size = value ?? ""; Invalidate(); } }
        protected override void OnClick(EventArgs e) { if (Enabled) { Focus(); Checked = !Checked; } base.OnClick(e); }
        protected override void OnDoubleClick(EventArgs e) { OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && Enabled) { Checked = !Checked; e.Handled = true; }
            else if ((e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) && Parent != null) { Parent.SelectNextControl(this, e.KeyCode == Keys.Down, true, false, false); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            var card = new RectangleF(0, 0, Width - 1, Height - 1);
            // 3.16: a neutral row; the tick box alone shows the choice (the accent tint and edge made a list of
            // ticked rows look like a list of warnings)
            Color fill = hover ? Theme.PanelHover : Theme.Panel;
            using (var p = Gfx.Round(card, Theme.S(Theme.Radius))) using (var b = new SolidBrush(fill)) g.FillPath(b, p);
            int pad = Theme.S(16), box = Theme.S(20);
            var br = new RectangleF(pad, (Height - box) / 2f, box, box);
            Box(g, br, on, hover, Enabled, fill);
            g.SmoothingMode = old;

            int tx = pad + box + Theme.S(14), sizeW = Theme.S(120), right = Width - pad;
            var szRect = new Rectangle(right - sizeW, 0, sizeW, Height);
            bool none = string.IsNullOrEmpty(size) || size == "—" || size == "0 B";
            TextRenderer.DrawText(g, L.T(size), Theme.Mono, szRect, none ? Theme.Muted : on ? Theme.Strong : Theme.Text, fill,   // tabular digits, so sizes line up
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            int tw = Math.Max(10, szRect.X - Theme.S(12) - tx);
            var tf = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (string.IsNullOrEmpty(detail))
                TextRenderer.DrawText(g, L.T(Text), Theme.FB, new Rectangle(tx, 0, tw, Height), Theme.Text, fill, tf | TextFormatFlags.VerticalCenter);
            else
            {
                int h1 = TextRenderer.MeasureText("Ag", Theme.FB).Height, h2 = TextRenderer.MeasureText("Ag", Theme.FS).Height, gap = Theme.S(2);
                int y = (Height - h1 - h2 - gap) / 2;
                TextRenderer.DrawText(g, L.T(Text), Theme.FB, new Rectangle(tx, y, tw, h1), Theme.Text, fill, tf);
                TextRenderer.DrawText(g, L.T(detail), Theme.FS, new Rectangle(tx, y + h1 + gap, tw, h2), Warning ? Theme.Warn : Theme.Muted, fill, tf);
            }
            if (Focused && ShowFocusCues) Gfx.FocusRing(g, new RectangleF(1, 1, Width - 3, Height - 3), Theme.S(Theme.Radius));
        }
        // Carbon check box: off = 1.5px outline (Muted, Text on hover), on = Accent fill + OnAccent tick.
        public static void Box(Graphics g, RectangleF r, bool on, bool hover, bool enabled, Color under)
        {
            float u = r.Height / 20f;
            using (var p = Gfx.Round(r, 4 * u))
            {
                if (on)
                {
                    Color c = hover ? Theme.AccentDark : Theme.Accent;
                    if (!enabled) c = Gfx.Mix(c, under, 0.6);
                    using (var b = new SolidBrush(c)) g.FillPath(b, p);
                    using (var pen = new Pen(enabled ? Theme.OnAccent : Gfx.Mix(Theme.OnAccent, under, 0.5), 2.2f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawLines(pen, new[] { new PointF(r.X + 5 * u, r.Y + 10.5f * u), new PointF(r.X + 8.5f * u, r.Y + 14 * u), new PointF(r.X + 15 * u, r.Y + 6.5f * u) });
                }
                else
                {
                    if (hover && enabled) using (var b = new SolidBrush(Gfx.Mix(under, Theme.Text, 0.08))) g.FillPath(b, p);
                    using (var pen = new Pen(enabled ? (hover ? Theme.Text : Theme.Muted) : Gfx.Mix(Theme.Muted, under, 0.6), 1.5f * u)) g.DrawPath(pen, p);
                }
            }
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new RowAcc(this); }
        class RowAcc : ControlAccessibleObject
        {
            readonly CheckRow r;
            public RowAcc(CheckRow r) : base(r) { this.r = r; }
            public override AccessibleRole Role { get { return AccessibleRole.CheckButton; } }
            public override string Value { get { return r.size; } }
            public override AccessibleStates State { get { var s = base.State | AccessibleStates.Focusable; if (r.Checked) s |= AccessibleStates.Checked; if (r.Focused) s |= AccessibleStates.Focused; return s; } }
            public override string DefaultAction { get { return r.Checked ? "Clear" : "Select"; } }
            public override void DoDefaultAction() { r.Checked = !r.Checked; }
        }
    }

    // Vertical list of CheckRows that keeps every row as wide as the visible area.
    public class CheckList : FlowLayoutPanel
    {
        public CheckList()
        {
            Dock = DockStyle.Fill; FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoScroll = true;
            BackColor = Theme.Bg; Padding = new Padding(0, 0, Theme.S(8), Theme.S(8));
            DoubleBuffered = true;
        }
        int RowWidth() { return Math.Max(Theme.S(360), Math.Min(Theme.S(Theme.MaxContent), ClientSize.Width - Padding.Horizontal - SystemInformation.VerticalScrollBarWidth)); }   // 3.14: readable width
        public void Fit()
        {
            int w = RowWidth();
            foreach (Control c in Controls) if (c.Width != w) c.Width = w;
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Fit(); }
        protected override void OnControlAdded(ControlEventArgs e) { e.Control.Width = RowWidth(); base.OnControlAdded(e); }
    }
}
