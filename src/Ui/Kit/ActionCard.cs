// 3.44: a Quick actions card on Home: an icon, a title and one line that says what the action does. A button to
// screen readers and the keyboard (Tab, Enter, Space). Built at 96 dpi like every page control; it paints in proportion
// to its size, so the page's one DPI pass scales it.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ArchTweaker
{
    public class ActionCard : Control
    {
        readonly string title, desc; readonly char glyph;
        bool hover, down;
        public event EventHandler Activated;

        public ActionCard(string title, string desc, char glyph, EventHandler click)
        {
            this.title = title; this.desc = desc; this.glyph = glyph;
            if (click != null) Activated += click;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(330, 66); Margin = new Padding(0, 0, 0, 8); Cursor = Cursors.Hand; TabStop = true;
            BackColor = Theme.Bg; Font = Theme.F;
            AccessibleRole = AccessibleRole.PushButton; AccessibleName = L.T(title); AccessibleDescription = L.T(desc);
            Name = title;   // UIA AutomationId
            Text = title;
        }

        void Fire() { var h = Activated; if (h != null) h(this, EventArgs.Empty); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool was = down; down = false; Invalidate();
            if (was && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) Fire();
            base.OnMouseUp(e);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { Fire(); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override AccessibleObject CreateAccessibilityInstance() { return new CardAccess(this); }

        sealed class CardAccess : ControlAccessibleObject
        {
            readonly ActionCard c;
            public CardAccess(ActionCard c) : base(c) { this.c = c; }
            public override string DefaultAction { get { return L.T("Press"); } }
            public override void DoDefaultAction() { c.Fire(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            float u = Height / 66f;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 3 * u);
            Color fill = down ? Theme.Grid : hover ? Theme.PanelHover : Theme.Panel;
            Gfx.Surface(g, r, fill, !down);
            int x = (int)(14 * u);
            var icf = NavItem.IconFont;
            if (icf != null && glyph != '\0')
            {
                using (var f = new Font(icf.FontFamily, 18 * u, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, glyph.ToString(), f, new Rectangle(x, 0, (int)(26 * u), (int)r.Height), Theme.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                x += (int)(38 * u);
            }
            int w = Width - x - (int)(26 * u);
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, L.T(title), Theme.FB, new Rectangle(x, (int)(8 * u), w, (int)(20 * u)), Theme.Text, flags);
            // the description may take two lines (longer languages); it is cut with "…" only after that
            TextRenderer.DrawText(g, L.T(desc), Theme.FS, new Rectangle(x, (int)(28 * u), w, Theme.FS.Height * 2 + 2), Theme.Muted,
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
            using (var pen = new Pen(Theme.Muted, 1.6f * u)) { float cx = Width - 16 * u, cy = r.Height / 2; g.DrawLines(pen, new[] { new PointF(cx - 3 * u, cy - 5 * u), new PointF(cx + 2 * u, cy), new PointF(cx - 3 * u, cy + 5 * u) }); }
            if (Focused && ShowFocusCues) Gfx.FocusRing(g, RectangleF.Inflate(r, -1, -1), 6 * u);
        }
    }
}
