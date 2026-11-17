// ArchTweaker: Impact meter and the small chips on tweak rows.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArchTweaker
{
    // Three bars, like a signal meter: how much speed a tweak is expected to give.
    public class ImpactMeter : Control
    {
        public int Level;
        public ImpactMeter(int level)
        {
            Level = level;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(18, 14); BackColor = Color.Transparent;
            AccessibleName = Word(level); AccessibleRole = AccessibleRole.Indicator;
        }
        public static string Word(int l) { return l >= 3 ? "High gain" : l == 2 ? "Medium gain" : l == 1 ? "Low gain" : "No speed effect"; }
        protected override void OnPaint(PaintEventArgs e) { Draw(e.Graphics, new RectangleF(0, 0, Width, Height), Level); }
        // three bars in an 18×14 (scaled) box
        public static void Draw(Graphics g, RectangleF box, int level)
        {
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            float u = box.Height / 14f;
            for (int i = 0; i < 3; i++)
            {
                float h = (5 + i * 4) * u, x = box.X + i * 6 * u;
                Color c = i < level ? (level >= 3 ? Theme.Ok : Theme.Text) : Theme.AccentOff;   // 3.1: no red — more bars = more gain
                using (var b = new SolidBrush(c)) using (var p = Gfx.Round(new RectangleF(x, box.Bottom - h, 4 * u, h), 1.5f * u)) g.FillPath(b, p);
            }
            g.SmoothingMode = old;
        }
    }

    // Small rounded tag: risk, restart, etc.
    public class Chip : Label
    {
        public Chip(string text, Color fg)
        {
            Text = text; ForeColor = fg; AutoSize = true; Font = Theme.FT; UseMnemonic = false;
            Padding = new Padding(7, 2, 7, 3); BackColor = Color.Transparent;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaint(PaintEventArgs e) { Draw(e.Graphics, ClientRectangle, Text, ForeColor, Parent != null ? Parent.BackColor : Theme.Panel); }
        public override Size GetPreferredSize(Size proposed) { return Measure(Text); }   // 3.16: room for the glyph
        public static Font ChipFont { get { return Theme.FT; } }
        // 3.16: a glyph before the text of risk and restart tags (Segoe Fluent Icons / MDL2; none if the font is missing)
        static readonly Dictionary<string, char> GlyphKeys = new Dictionary<string, char>
        {
            { "Danger", '\uE7BA' }, { "Moderate risk", '\uE7BA' }, { "Advanced", '\uE72E' }, { "No undo", '\uE7BA' },
            { "Experimental", '\uE9D9' }, { "Documented", '\uE8A5' }, { "Measured", '\uE9D2' }, { "Unproven", '\uE897' }, { "Restart", '\uE777' }, { "Sign-out", '\uE77B' }, { "Covered", '\uE73E' },
        };
        static Dictionary<string, char> glyphs; static string glyphLang; static Font glyphFont; static int glyphDpi;
        static bool GlyphOf(string text, out char c)
        {
            c = '\0'; if (NavItem.IconFont == null || text == null) return false;
            if (glyphs == null || glyphLang != L.Lang) { glyphs = new Dictionary<string, char>(); foreach (var kv in GlyphKeys) glyphs[L.T(kv.Key)] = kv.Value; glyphLang = L.Lang; }
            return glyphs.TryGetValue(text, out c);
        }
        static Font GlyphFont
        {
            get { if (glyphFont == null || glyphDpi != Theme.Dpi) { glyphDpi = Theme.Dpi; glyphFont = new Font(NavItem.IconFont.FontFamily, Theme.S(10f), GraphicsUnit.Pixel); } return glyphFont; }
        }
        public static Size Measure(string text)
        {
            var sz = TextRenderer.MeasureText(text, ChipFont, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            char c; int gw = GlyphOf(text, out c) ? Theme.S(14) : 0;
            return new Size(sz.Width + Theme.S(14) + gw, sz.Height + Theme.S(6));
        }
        public static void Draw(Graphics g, Rectangle r, string text, Color fg, Color under)
        {
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Gfx.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1.5f, r.Height - 1.5f), (r.Height - 2) / 2f))
            using (var b = new SolidBrush(Gfx.Mix(under, fg, 0.10)))
            using (var pen = new Pen(Gfx.Mix(under, fg, 0.45)))
            { g.FillPath(b, p); g.DrawPath(pen, p); }
            g.SmoothingMode = old;
            char c;
            if (GlyphOf(text, out c))
            {
                int gw = Theme.S(14);
                TextRenderer.DrawText(g, c.ToString(), GlyphFont, new Rectangle(r.X + Theme.S(6), r.Y, gw, r.Height), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, L.T(text), ChipFont, new Rectangle(r.X + gw, r.Y, r.Width - gw, r.Height), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }
            TextRenderer.DrawText(g, L.T(text), ChipFont, r, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
