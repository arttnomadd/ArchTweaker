// ArchTweaker: Settings card: a titled group with rows (Tools & backup, Appearance).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArchTweaker
{
    // Consecutive buttons share one wrapping action row. A label with Tag "note" becomes the description.
    public class SettingsCard : Panel
    {
        readonly FlowLayoutPanel body;
        readonly Label title, desc;
        FlowLayoutPanel actions;
        public static int Pad { get { return Theme.S(Theme.Sp.X16); } }
        // Shared code points of Segoe Fluent Icons (Win 11) and Segoe MDL2 Assets (Win 10), keyed by the English title
        static readonly Dictionary<string, char> Glyph = new Dictionary<string, char>
        {
            { "Language", '\uE774' }, { "Colour scheme", '\uE790' }, { "Progress bars", '\uE9D2' },
            { "Updates", '\uE895' }, { "Keep tweaks applied", '\uE72C' }, { "Scheduled maintenance", '\uE787' }, { "Support and tweak updates", '\uE896' }, { "Reports", '\uE8A5' },
            { "Safety and cleanup", '\uEA18' }, { "Help", '\uE897' }, { "Apply history", '\uE7A7' }, { "Restore points and profiles", '\uE81C' }, { "Drivers", '\uE964' }, { "NVIDIA profile (.nip)", '\uE7F4' },
            { "Leftovers", '\uE74D' }, { "Recovery kit", '\uE777' }, { "Self-test", '\uE9D9' }, { "Fixes", '\uE90F' }, { "Performance", '\uEC4A' },
            { "Power plan files", '\uE7E8' }, { "Windows shortcuts", '\uE71D' }, { "Command line", '\uE756' },
        };
        static Font glyphFont; static int glyphDpi;
        static Font GlyphFont
        {
            get
            {
                var nf = NavItem.IconFont; if (nf == null) return null;
                if (glyphFont == null || glyphDpi != Theme.Dpi) { glyphDpi = Theme.Dpi; glyphFont = new Font(nf.FontFamily, Theme.S(16f), GraphicsUnit.Pixel); }
                return glyphFont;
            }
        }
        // Margins and paddings below are 96-dpi pixels on purpose: the page-wide Theme.ScaleTree pass scales them once.
        // Using S() here scaled them twice above 100 % DPI (fixed in 3.14).
        public SettingsCard(string text)
        {
            DoubleBuffered = true; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Theme.Panel; Padding = new Padding(Theme.Sp.X16, Theme.Sp.X16, Theme.Sp.X16, Theme.Sp.X8 + 3); Margin = new Padding(0, 0, 0, Theme.Sp.X12);
            body = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Panel, Margin = Padding.Empty, Padding = Padding.Empty };
            title = new Label { Text = text, Font = Theme.FB, ForeColor = Theme.Strong, AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 0, 0, Theme.Sp.X4) };
            desc = new Label { Font = Theme.FS, ForeColor = Theme.Muted, AutoSize = true, UseMnemonic = false, Visible = false, Margin = new Padding(0, 0, 0, Theme.Sp.X12) };
            char gl; Font gf = GlyphFont;
            if (gf != null && Glyph.TryGetValue(text, out gl))
            {   // 3.11: a Segoe Fluent Icons glyph before the title; decorative, so screen readers skip it
                var head = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Panel, Margin = title.Margin, Padding = Padding.Empty };
                var ic = new Label { Text = gl.ToString(), Font = gf, ForeColor = Theme.Accent, AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 1, Theme.Sp.X8, 0), AccessibleRole = AccessibleRole.Graphic, Tag = "notr" };
                title.Margin = Padding.Empty;
                head.Controls.Add(ic); head.Controls.Add(title);
                body.Controls.Add(head);
            }
            else body.Controls.Add(title);
            body.Controls.Add(desc);
            body.Location = new Point(Padding.Left, Padding.Top);   // a Panel's Padding doesn't move its children
            Controls.Add(body);
        }
        public void Add(Control c)
        {
            var lb = c as Label;
            if (lb != null && "note".Equals(lb.Tag))
            {
                desc.Text = string.IsNullOrEmpty(desc.Text) ? lb.Text : desc.Text + " " + lb.Text; desc.Visible = true; return;
            }
            if (c is Button)
            {
                if (actions == null)
                {
                    actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Panel, Margin = new Padding(0, 2, 0, 4), Padding = Padding.Empty };
                    body.Controls.Add(actions);
                }
                c.Margin = new Padding(0, 0, Theme.Sp.X8, Theme.Sp.X8);
                actions.Controls.Add(c); return;
            }
            actions = null;   // a non-button row ends the current action row
            if (lb != null) lb.Margin = new Padding(lb.Margin.Left, 2, 0, 6);
            else c.Margin = new Padding(c.Margin.Left, 4, 0, 6);
            body.Controls.Add(c);
        }
        public void SetWidth(int w)
        {
            int inner = Math.Max(Theme.S(200), w - Padding.Horizontal);
            MinimumSize = new Size(w, 0); MaximumSize = new Size(w, 0);
            body.MinimumSize = new Size(inner, 0); body.MaximumSize = new Size(inner, 0);
            Fit(desc, inner);
            foreach (Control c in body.Controls)
            {
                if (c == title || c == desc) continue;
                if (c is FlowLayoutPanel) c.MaximumSize = new Size(inner, 0);
                else if (c is Label) Fit((Label)c, inner - c.Margin.Left);
                else if (c is CheckBox) c.MaximumSize = new Size(inner - c.Margin.Left, 0);
            }
        }
        // A wrapping label gets its height from its text at the card width.
        // AutoSize labels on Mono keep their old height when MaximumSize shrinks, so the second line was cut off.
        static void Fit(Label l, int max)
        {
            if (l.AutoSize) { l.AutoSize = false; l.TextChanged += (s, e) => Refit((Label)s); }
            l.MaximumSize = new Size(max, 0);
            Refit(l);
        }
        static void Refit(Label l)
        {
            int max = l.MaximumSize.Width;
            if (max <= 0) return;
            var p = l.GetPreferredSize(new Size(max, 0));
            l.Size = new Size(Math.Min(max, p.Width), p.Height);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent != null ? Parent.BackColor : Theme.Bg); g.SmoothingMode = SmoothingMode.AntiAlias;
            Gfx.Surface(g, new RectangleF(0, 0, Width - 1, Height - Theme.S(3)), Theme.Panel, true);   // 3.36
        }
    }
}
