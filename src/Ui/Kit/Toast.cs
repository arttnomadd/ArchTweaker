// ArchTweaker: Toast messages at the bottom of the window.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ---- result pop-up (2.9): bottom-right of the window, closes itself after 6 s or on click
    public class Toast : Control
    {
        public enum Kind { Ok, Warn, Bad }
        Kind kind; string text = "";
        readonly Timer hide = new Timer { Interval = 6000 };
        // 3.2: an optional action on the right (Undo after Apply); the toast then stays 12 s
        string actionText; Func<bool> action; Rectangle actionR; bool hoverAction;   // action returns false = couldn't run yet: keep it
        public Toast()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Visible = false; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.Alert; AccessibleName = "Result";
            hide.Tick += delegate { hide.Stop(); Dismiss(); };
            MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (action != null && actionR.Contains(e.Location)) { TryAction(); return; }
                hide.Stop(); Dismiss();
            };
            MouseMove += (s, e) => { bool h = action != null && actionR.Contains(e.Location); if (h != hoverAction) { hoverAction = h; Invalidate(); } };
            MouseLeave += delegate { if (hoverAction) { hoverAction = false; Invalidate(); } };
            TabStop = false;
            Disposed += delegate { hide.Dispose(); };
        }
        public void Show(string message, Kind k) { Show(message, k, null, null); }
        public void Show(string message, Kind k, string actText, Func<bool> act)
        {
            Motion.Stop(this);   // 3.45: a running exit must not hide the new message
            text = message; kind = k; actionText = act != null ? actText : null; action = act; hoverAction = false;
            AccessibleName = message + (act != null ? " — " + actText + ": Ctrl+Z" : "");
            var sz = TextRenderer.MeasureText(message, Theme.F, new Size(Theme.S(360), 1000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int aw = act != null ? TextRenderer.MeasureText(actText, Theme.FB, Size.Empty, TextFormatFlags.NoPrefix).Width + Theme.S(28) : 0;
            Size = new Size(Math.Max(Theme.S(220), sz.Width + Theme.S(56) + aw), sz.Height + Theme.S(28));
            actionR = act != null ? new Rectangle(Width - aw - Theme.S(4), 0, aw, Height) : Rectangle.Empty;
            Visible = true; BringToFront(); Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.SystemAlert, -1);
            hide.Stop(); hide.Interval = act != null ? 12000 : 6000; hide.Start();
            var h = Placed; if (h != null) h(this, EventArgs.Empty);
            // 3.16: slides up 16 px into place over 150 ms (none when Windows animations are off)
            if (Motion.On)
            {
                int y1 = Top, dy = Theme.S(16); double t = 0; Top = y1 + dy;
                Motion.Run(this, dt => { t = Math.Min(1, t + dt / Theme.Ms.Small); double e = 1 - Math.Pow(1 - t, 3); Top = y1 + (int)Math.Round(dy * (1 - e)); return t >= 1; });
            }
        }
        // 3.45: leaving drops 8 px over 120 ms (ease-out, faster than the 150 ms entry), then hides; a new Show
        // replaces the exit. Instant when Windows animations are off.
        void Dismiss()
        {
            if (!Visible) return;
            if (!Motion.On || !IsHandleCreated) { Visible = false; return; }
            int y0 = Top, dy = Theme.S(8); double t = 0;
            Motion.Run(this, dt =>
            {
                t = Math.Min(1, t + dt / 120.0); double e = 1 - Math.Pow(1 - t, 3);
                Top = y0 + (int)Math.Round(dy * e);
                if (t >= 1) { Visible = false; Top = y0; }
                return t >= 1;
            });
        }
        public event EventHandler Placed;     // the form positions the toast (bottom-right, above the Apply bar)
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color edge = kind == Kind.Bad ? Theme.Bad : kind == Kind.Warn ? Theme.Warn : Theme.Accent;
            using (var p = Gfx.Round(r, Theme.S(Theme.Radius)))
            {
                using (var b = new SolidBrush(Theme.Panel)) g.FillPath(b, p);
                using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, p);
            }
            using (var b = new SolidBrush(edge)) g.FillRectangle(b, 1, Theme.S(6), Theme.S(3), Height - Theme.S(12));
            // status glyph: tick / exclamation drawn with lines (no icon font needed)
            float cx = Theme.S(22), cy = Height / 2f, u = Theme.Scale;
            using (var pen = new Pen(edge, 2f * u))
            {
                if (kind == Kind.Ok) g.DrawLines(pen, new[] { new PointF(cx - 5 * u, cy), new PointF(cx - 1.5f * u, cy + 3.5f * u), new PointF(cx + 5 * u, cy - 4 * u) });
                else { g.DrawLine(pen, cx, cy - 6 * u, cx, cy + 1.5f * u); using (var dot = new SolidBrush(edge)) g.FillEllipse(dot, cx - 1.4f * u, cy + 3.5f * u, 2.8f * u, 2.8f * u); }
            }
            TextRenderer.DrawText(g, L.T(text), Theme.F, new Rectangle(Theme.S(38), Theme.S(14), Width - Theme.S(52) - actionR.Width, Height - Theme.S(24)), Theme.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            if (action != null)
            {
                using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, actionR.X, Theme.S(8), actionR.X, Height - Theme.S(8));
                var f = hoverAction ? new Font(Theme.FB, FontStyle.Underline) : Theme.FB;
                TextRenderer.DrawText(g, actionText, f, actionR, Theme.Strong, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                if (hoverAction) f.Dispose();
            }
        }
        // Ctrl+Z while the toast offers an action (Undo)
        public bool TryAction()
        {
            if (!Visible || action == null) return false;
            if (!action()) return true;           // busy: the toast keeps its button
            action = null; hide.Stop(); Dismiss(); return true;
        }
    }
}
