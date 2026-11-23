// ArchTweaker: Switch controls: the tweak toggle, the settings switch and the tick box.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArchTweaker
{
    public class Toggle : Control
    {
        bool on, hover; float pos;
        public event EventHandler CheckedChanged;
        public Toggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(44, 26); Cursor = Cursors.Hand; TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton;
        }
        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; Motion.Slide(this, () => pos, v => pos = v, on, ClientRectangle); AccessibilityNotifyClients(AccessibleEvents.StateChange, -1); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); }
        }
        protected override void OnClick(EventArgs e) { if (Enabled) { Focus(); Checked = !Checked; } base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space && Enabled) { Checked = !Checked; e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (!Motion.Running(this)) pos = on ? 1f : 0f;
            Draw(e.Graphics, new RectangleF(0, 0, Width, Height), pos, hover, Enabled, Parent != null ? Parent.BackColor : Theme.Panel, false);
            if (Focused && ShowFocusCues) Gfx.FocusRing(e.Graphics, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), (Height - 1.5f) / 2f);
        }
        // Shared switch painter (also used by the owner-drawn TweakRow). `box` is the 44×26 (scaled) switch cell.
        // Windows 11 pattern: off = outlined track + dark knob, on = filled accent + white knob (both ≥ 3:1 against the row, WCAG 1.4.11).
        // unknown = state still being read: outlined track, no knob, three dots.
        public static void Draw(Graphics g, RectangleF box, bool on, bool hover, bool enabled, Color under, bool unknown) { Draw(g, box, on ? 1f : 0f, hover, enabled, under, unknown); }
        // pos: 0 = off, 1 = on, in between while the knob slides (track fills in, knob colour blends)
        public static void Draw(Graphics g, RectangleF box, float pos, bool hover, bool enabled, Color under, bool unknown)
        {
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            float u = box.Height / 26f;
            var r = new RectangleF(box.X + 2 * u, box.Y + 3 * u, box.Width - 5 * u, box.Height - 7 * u);
            Color knob;
            using (var p = Gfx.Round(r, r.Height / 2))
            {
                if (unknown)
                {
                    using (var pen = new Pen(Gfx.Mix(Theme.Muted, under, 0.5), 1.5f * u)) g.DrawPath(pen, p);
                    // 3.44: the three dots pulse one after another while the state is read (still when animations are off)
                    double t = Motion.On ? Environment.TickCount / 360.0 : 0;
                    for (int i = -1; i <= 1; i++)
                    {
                        double w = Motion.On ? 0.45 + 0.55 * Math.Max(0, Math.Cos(t - (i + 1) * 0.9)) : 1;
                        using (var b = new SolidBrush(Gfx.Mix(under, Theme.Muted, w)))
                            g.FillEllipse(b, r.X + r.Width / 2 + i * 6 * u - 1.5f * u, r.Y + r.Height / 2 - 1.5f * u, 3 * u, 3 * u);
                    }
                    g.SmoothingMode = old; return;
                }
                Color edge = enabled ? (hover ? Theme.Text : Theme.Muted) : Gfx.Mix(Theme.Muted, under, 0.6);
                Color track = hover ? Theme.OnHover : Theme.On;
                if (!enabled) track = Gfx.Mix(track, under, 0.6);
                Color onKnob = enabled ? Theme.OnInk : Gfx.Mix(Theme.OnInk, under, 0.5);
                if (pos < 1f)
                {   // off look: outlined track
                    if (hover && enabled) using (var b = new SolidBrush(Gfx.Mix(under, Theme.Text, 0.08))) g.FillPath(b, p);
                    using (var pen = new Pen(edge, 1.5f * u)) g.DrawPath(pen, p);
                }
                if (pos > 0f) using (var b = new SolidBrush(Color.FromArgb((int)(255 * Math.Min(1f, pos)), track))) g.FillPath(b, p);   // fills in as it turns on
                knob = Gfx.Mix(edge, onKnob, Math.Max(0f, Math.Min(1f, pos)));
            }
            float k = r.Height - 8 * u, x0 = r.X + 4 * u, x1 = r.Right - k - 4 * u, x = x0 + (x1 - x0) * Math.Max(0f, Math.Min(1f, pos));
            using (var b = new SolidBrush(knob)) g.FillEllipse(b, x, r.Y + 4 * u, k, k);
            g.SmoothingMode = old;
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new ToggleAcc(this); }
        class ToggleAcc : ControlAccessibleObject
        {
            readonly Toggle t;
            public ToggleAcc(Toggle t) : base(t) { this.t = t; }
            public override AccessibleRole Role { get { return AccessibleRole.CheckButton; } }
            public override AccessibleStates State { get { var s = base.State | AccessibleStates.Focusable; if (t.Checked) s |= AccessibleStates.Checked; if (!t.Enabled) s |= AccessibleStates.Unavailable; if (t.Focused) s |= AccessibleStates.Focused; return s; } }
            public override string DefaultAction { get { return t.Checked ? "Turn off" : "Turn on"; } }
            public override void DoDefaultAction() { if (t.Enabled) t.Checked = !t.Checked; }
        }
    }

    // ---- 3.10.5: a CheckBox drawn as the app's switch, so settings pages keep CheckBox events and look like the tweak rows
    public class SwitchBox : CheckBox
    {
        bool hover;
        static int SW { get { return Theme.S(44); } }
        static int SH { get { return Theme.S(26); } }
        static int Gap { get { return Theme.S(10); } }
        public SwitchBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = true; Cursor = Cursors.Hand; Font = Theme.F;
        }
        public override Size GetPreferredSize(Size proposed)
        {
            int max = MaximumSize.Width > 0 ? MaximumSize.Width : Theme.S(860);
            var ts = TextRenderer.MeasureText(Text, Font, new Size(Math.Max(10, max - SW - Gap), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return new Size(Math.Min(max, SW + Gap + ts.Width + 2), Math.Max(SH, ts.Height + Theme.S(6)));
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Color under = Parent != null ? Parent.BackColor : Theme.Panel; g.Clear(under);
            var box = new RectangleF(0, Math.Max(0, (Math.Min(Height, Theme.S(32)) - SH) / 2f), SW, SH);
            Toggle.Draw(g, box, Checked, hover, Enabled, under, false);
            if (Focused && ShowFocusCues) Gfx.FocusRing(g, RectangleF.Inflate(box, -1, -2), (SH - 4) / 2f);
            var tr = new Rectangle(SW + Gap, 0, Width - SW - Gap, Height);
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? Theme.Text : Theme.Muted, under, TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // 3.14: a themed check box (Carbon: 16 px square, accent fill and a tick when on). The native one stays white
    // on dark schemes. Use it for an option of an action or a confirmation; a setting that acts at once is a SwitchBox.
    public class TickBox : CheckBox
    {
        bool hover;
        static int Box { get { return Theme.S(16); } }
        static int Gap { get { return Theme.S(8); } }
        public TickBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }
        public override Size GetPreferredSize(Size proposed)
        {
            int max = MaximumSize.Width > 0 ? MaximumSize.Width : Theme.S(900);
            var ts = TextRenderer.MeasureText(Text.Length == 0 ? " " : Text, Font, new Size(Math.Max(10, max - Box - Gap), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return new Size(Math.Min(max, Box + (Text.Length == 0 ? 0 : Gap + ts.Width) + 2), Math.Max(Theme.S(24), ts.Height + Theme.S(6)));   // 24 px target (WCAG 2.5.8)
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Color under = Parent != null ? Parent.BackColor : Theme.Panel; g.Clear(under);
            int line = Math.Max(Box, Font.Height), top = Theme.S(3);
            var b = new Rectangle(0, top + (line - Box) / 2, Box, Box);
            Color edge = !Enabled ? Theme.Border : Checked ? Theme.Accent : hover ? Theme.Strong : Theme.Muted;
            using (var br = new SolidBrush(Checked && Enabled ? Theme.Accent : under)) g.FillRectangle(br, b);
            using (var pen = new Pen(edge)) g.DrawRectangle(pen, b.X, b.Y, b.Width - 1, b.Height - 1);
            if (Checked)
            {
                var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
                float u = Box / 16f;
                using (var pen = new Pen(Enabled ? Theme.OnAccent : Theme.Muted, Math.Max(1.6f, 1.8f * u)))
                    g.DrawLines(pen, new[] { new PointF(b.X + 3.5f * u, b.Y + 8.2f * u), new PointF(b.X + 6.6f * u, b.Y + 11.2f * u), new PointF(b.X + 12.4f * u, b.Y + 4.8f * u) });
                g.SmoothingMode = old;
            }
            if (Focused && ShowFocusCues) Gfx.FocusRing(g, new RectangleF(b.X + 1, b.Y + 1, b.Width - 2, b.Height - 2), 0);
            var tr = new Rectangle(Box + Gap, top, Width - Box - Gap, Height - top);
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? ForeColor : Theme.Muted, under, TextFormatFlags.WordBreak | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
        }
    }
}
