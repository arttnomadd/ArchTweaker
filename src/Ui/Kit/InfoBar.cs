// ArchTweaker: Info bar for notices inside a page.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ---- 3.10.5: settings cards (Tools & backup page). A rounded panel: title, a muted description, then rows.
    // 3.12: one inline status bar for long-lived app state (Windows InfoBar pattern): a severity stripe, a glyph and a
    // light tint of the severity colour. The words carry the meaning; colour and glyph only support them.
    public class InfoBar : Panel
    {
        public enum Kind { Info, Success, Warning, Error }
        Kind kind;
        public Kind Severity { get { return kind; } set { kind = value; BackColor = Tint; foreach (Control c in Controls) c.BackColor = Color.Transparent; Invalidate(); } }
        public InfoBar(Kind k)
        {
            DoubleBuffered = true; Dock = DockStyle.Top; Height = 46; Visible = false;
            Padding = new Padding(52, 8, 16, 8); AccessibleRole = AccessibleRole.Alert;
            Severity = k;
        }
        // 3.36.1: text that wraps, buttons pinned to the right. The text gets the width left of the buttons and the bar
        // grows to fit it, so a long message (Russian is about 30 % longer) never pushes a button out of the window.
        public void Compose(Label text, params Control[] buttons)
        {
            var btns = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty, Padding = new Padding(12, 0, 0, 0) };
            foreach (var b in buttons) { b.Margin = new Padding(0, 0, 8, 0); btns.Controls.Add(b); }
            text.AutoSize = false; text.Dock = DockStyle.Fill; text.TextAlign = ContentAlignment.MiddleLeft; text.AutoEllipsis = false; text.Margin = Padding.Empty;
            text.BackColor = Color.Transparent;
            Controls.Add(text); Controls.Add(btns);   // the right-docked buttons lay out first, the text fills the rest
            EventHandler fit = delegate
            {
                if (IsDisposed) return;
                int w = Math.Max(Theme.S(120), ClientSize.Width - Padding.Horizontal - btns.Width);
                int th = TextRenderer.MeasureText(text.Text ?? "", text.Font, new Size(w, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
                int bh = buttons.Length == 0 ? 0 : buttons.Max(b => b.Height);
                int want = Math.Max(Theme.S(46), Math.Max(th, bh) + Padding.Vertical + Theme.S(4));
                if (Height != want) Height = want;
                btns.Padding = new Padding(btns.Padding.Left, Math.Max(0, (ClientSize.Height - Padding.Vertical - bh) / 2), 0, 0);
            };
            Resize += fit; text.TextChanged += fit; text.FontChanged += fit; btns.SizeChanged += fit; VisibleChanged += fit;
        }

        public Color Tone { get { return kind == Kind.Error ? Theme.Bad : kind == Kind.Warning ? Theme.Warn : kind == Kind.Success ? Theme.Ok : Theme.Accent; } }
        Color Tint { get { return Theme.HighContrast ? SystemColors.Info : Gfx.Mix(Theme.Bg, Tone, Theme.Dark ? 0.14 : 0.10); } }
        char Glyph { get { return kind == Kind.Error ? '\uEA39' : kind == Kind.Warning ? '\uE7BA' : kind == Kind.Success ? '\uE73E' : '\uE946'; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            // 3.36: the tint and the glyph carry the severity; hairlines in the tone close the bar top and bottom
            using (var pen = new Pen(Gfx.Mix(Tone, Theme.Bg, 0.55))) { g.DrawLine(pen, 0, 0, Width, 0); g.DrawLine(pen, 0, Height - 1, Width, Height - 1); }
            var f = NavItem.IconFont;
            if (f != null)
                using (var big = new Font(f.FontFamily, Theme.S(16f), GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, Glyph.ToString(), big, new Rectangle(Theme.S(18), 0, Theme.S(24), Height), Tone, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            base.OnPaint(e);
        }
    }
}
