// ArchTweaker: FocusButton and ThemeSwitch: the visible dark/light switch in the top bar (3.32 split from Theme.cs).
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
    // Visible dark/light switch in the top bar.
    // Flat button with the app's 2px focus ring (flat WinForms buttons only draw a faint dotted rectangle)
    // 3.36: drawn here, not by Windows: rounded, a hairline border, a light top edge, and real hover / press states.
    // The colours still come from BackColor and FlatAppearance (Theme.Btn / Primary / Lead set them), so callers that
    // recolour a button keep working. A button with an image is left to WinForms.
    public class FocusButton : Button
    {
        bool hot, down;
        public bool Chevron;   // a "›" at the right edge: the button opens another page
        public FocusButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { keyDown = false; Invalidate(); base.OnLostFocus(e); }
        // the press shows while the left button is held over the button (leaving and coming back keeps it), or Space is held
        bool keyDown;
        // 3.45: a quick click still shows the press for at least 100 ms, so the button visibly answers
        bool hold; int downAt; System.Windows.Forms.Timer holdTimer;
        bool Pressed { get { return keyDown || hold || down && hot; } }
        void HoldPress()
        {
            int left = 100 - unchecked(Environment.TickCount - downAt);
            if (left <= 10 || !hot) return;
            hold = true;
            if (holdTimer == null) { holdTimer = new System.Windows.Forms.Timer(); holdTimer.Tick += delegate { holdTimer.Stop(); hold = false; Invalidate(); }; Disposed += delegate { holdTimer.Dispose(); }; }
            holdTimer.Stop(); holdTimer.Interval = left; holdTimer.Start();
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; downAt = Environment.TickCount; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { if (down) HoldPress(); down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) down = false; Invalidate(); base.OnMouseCaptureChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space && !keyDown) { keyDown = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space && keyDown) { keyDown = false; Invalidate(); } base.OnKeyUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { hot = down = keyDown = hold = false; Invalidate(); base.OnEnabledChanged(e); }
        public override Size GetPreferredSize(Size proposed)
        {
            var s = base.GetPreferredSize(proposed);
            if (Chevron) s.Width += Theme.S(16);
            return s;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Image != null || BackgroundImage != null) { base.OnPaint(e); PaintFocus(e.Graphics); return; }
            var g = e.Graphics;
            Color under = Theme.Bg;   // the first opaque colour behind the button (a transparent bar inside a tinted one)
            for (var p0 = Parent; p0 != null; p0 = p0.Parent) if (p0.BackColor.A == 255) { under = p0.BackColor; break; }
            g.Clear(under);
            bool primary = Enabled && BackColor == Theme.Accent;
            bool lead = Enabled && !primary && FlatAppearance.BorderSize >= 2;
            Color fill = !Enabled ? Gfx.Mix(under, Theme.Panel, 0.6) : Pressed ? FlatAppearance.MouseDownBackColor : hot ? FlatAppearance.MouseOverBackColor : BackColor;
            if (fill.A == 0) fill = under;
            Color edge = primary ? Gfx.Mix(fill, Color.Black, 0.18) : lead ? Theme.Accent : hot && Enabled ? Gfx.Mix(Theme.Line, Theme.Text, 0.25) : Theme.Line;
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = Theme.S((float)Theme.Radius);
            // 3.45: pressed, the shape sits 1 px in on every side (the desktop form of scale 0.97)
            float inset = Pressed ? Math.Max(1f, Theme.S(1f)) : 0f;
            using (var p = Gfx.Round(new RectangleF(inset, inset, Width - 2 * inset, Height - 2 * inset), rad)) using (var b = new SolidBrush(fill)) g.FillPath(b, p);
            if (Enabled && !Pressed && (primary || Theme.Dark) && Theme.ShadowA > 0)
            {   // light top edge: brighter on the accent, a whisper on neutral buttons
                Color hi = primary ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(Theme.Dark ? 26 : 0, 255, 255, 255);
                using (var pen = new Pen(hi)) g.DrawLine(pen, rad, 0.5f, Width - rad, 0.5f);
            }
            float bw = lead ? Math.Max(1.5f, Theme.S(1.5f)) : 1f;
            using (var p = Gfx.Round(new RectangleF(bw / 2 + inset, bw / 2 + inset, Width - bw - 2 * inset, Height - bw - 2 * inset), rad)) using (var pen = new Pen(edge, bw)) g.DrawPath(pen, p);
            g.SmoothingMode = old;
            var tr = new Rectangle(Padding.Left, 0, Width - Padding.Horizontal - (Chevron ? Theme.S(16) : 0), Height);
            if (Pressed) tr.Offset(0, 1);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine
                      | (TextAlign == ContentAlignment.MiddleLeft || TextAlign == ContentAlignment.TopLeft || TextAlign == ContentAlignment.BottomLeft ? TextFormatFlags.Left
                       : TextAlign == ContentAlignment.MiddleRight ? TextFormatFlags.Right : TextFormatFlags.HorizontalCenter);
            Color ink = !Enabled ? Theme.Muted : ForeColor;
            TextRenderer.DrawText(g, Text, Font, tr, ink, flags);
            if (Chevron)
            {
                float cx = Width - Theme.S(14f), cy = Height / 2f, u = Theme.Scale;
                using (var pen = new Pen(hot ? Theme.Strong : Theme.Muted, Math.Max(1.5f, 1.5f * u)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(pen, new[] { new PointF(cx - 2 * u, cy - 4 * u), new PointF(cx + 2 * u, cy), new PointF(cx - 2 * u, cy + 4 * u) });
            }
            PaintFocus(g);
        }
        void PaintFocus(Graphics g) { if (Focused && ShowFocusCues) Gfx.FocusRing(g, new RectangleF(1, 1, Width - 3, Height - 3), Theme.S(Theme.Radius)); }
    }


    public class ThemeSwitch : Control
    {
        public ThemeSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(124, 30); Cursor = Cursors.Hand; BackColor = Theme.Bg; TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton; AccessibleName = "Dark mode"; AccessibleDescription = "Switch between dark and light colours";
        }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override AccessibleObject CreateAccessibilityInstance() { return new SwAcc(this); }
        class SwAcc : ControlAccessibleObject
        {
            public SwAcc(Control c) : base(c) { }
            public override AccessibleStates State { get { var s = base.State | AccessibleStates.Focusable; if (Theme.Dark) s |= AccessibleStates.Checked; return s; } }
        }
        static System.Drawing.Drawing2D.GraphicsPath Pill(RectangleF r)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath(); float d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 90, 180); p.AddArc(r.Right - d, r.Y, d, d, 270, 180); p.CloseFigure(); return p;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            string label = Theme.Dark ? "Dark" : "Light";
            TextRenderer.DrawText(g, L.T(label), Theme.FB, new Rectangle(0, 0, 64, Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            var track = new RectangleF(70, 5, 50, Height - 10);
            using (var path = Pill(track)) { if (Theme.Dark) using (var br = new SolidBrush(Theme.Accent)) g.FillPath(br, path); else using (var pen = new Pen(Theme.Muted, 1.5f)) g.DrawPath(pen, path); }
            float k = track.Height - 6;
            float x = Theme.Dark ? track.Right - k - 3 : track.X + 3;
            using (var br = new SolidBrush(Theme.Dark ? Theme.OnAccent : Theme.Muted)) g.FillEllipse(br, x, track.Y + 3, k, k);
            // little sun / moon glyph inside the knob
            using (var br = new SolidBrush(Theme.Dark ? Theme.Accent : Theme.Panel))
            {
                if (Theme.Dark) { g.FillEllipse(br, x + k * 0.28f, track.Y + 3 + k * 0.22f, k * 0.5f, k * 0.5f); using (var w = new SolidBrush(Theme.OnAccent)) g.FillEllipse(w, x + k * 0.42f, track.Y + 3 + k * 0.14f, k * 0.46f, k * 0.46f); }
                else g.FillEllipse(br, x + k * 0.3f, track.Y + 3 + k * 0.3f, k * 0.4f, k * 0.4f);
            }
            if (Focused && ShowFocusCues) Gfx.FocusRing(g, new RectangleF(track.X - 3, track.Y - 3, track.Width + 6, track.Height + 6), (track.Height + 6) / 2f);
        }
    }
}
