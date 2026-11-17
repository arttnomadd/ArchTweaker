// ArchTweaker: Key-value card control (System info).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ================= SYSTEM INFO CARDS =================
    // One card of "name: value" rows with a Copy button. Laid out in S() units (the DPI scale pass skips it).
    public class KvCard : Control
    {
        readonly string title; readonly char glyph; readonly List<KeyValuePair<string, string>> rows;
        readonly List<int> rowH = new List<int>(); Rectangle copyR; bool hot, keys; DateTime copied = DateTime.MinValue;
        public KvCard(string title, char glyph, List<KeyValuePair<string, string>> rows)
        {
            this.title = title; this.glyph = glyph; this.rows = rows;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Bg; TabStop = true; Margin = new Padding(0, 0, 0, Theme.S(Theme.Sp.X12));
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = L.T(title) + ". " + string.Join(". ", rows.Select(r => (r.Key.Length > 0 ? r.Key + ": " : "") + r.Value).ToArray());
            AccessibleDescription = L.T("Press Enter to copy this card.");
        }
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified) { }
        protected override bool ScaleChildren { get { return false; } }
        static int Pad { get { return Theme.S(Theme.Sp.X16); } }
        static int KeyW { get { return Theme.S(170); } }
        public string CopyText { get { return L.T(title) + "\r\n" + string.Join("\r\n", rows.Select(r => r.Key.PadRight(22) + r.Value).ToArray()); } }
        public void Fit(int w)
        {
            rowH.Clear();
            int vw = Math.Max(Theme.S(120), w - Pad * 2 - KeyW), y = Pad + Theme.S(28);
            foreach (var r in rows)
            {
                int h = TextRenderer.MeasureText(r.Value.Length == 0 ? " " : r.Value, Theme.F, new Size(vw, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + Theme.S(Theme.Sp.X4);
                rowH.Add(h); y += h;
            }
            Size = new Size(w, y + Pad - Theme.S(Theme.Sp.X4) + Theme.S(3));   // 3.36: room for the surface shadow
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            Gfx.Surface(g, new RectangleF(0, 0, Width - 1, Height - Theme.S(3)), Theme.Panel, true);   // 3.36
            int x = Pad; var icf = NavItem.IconFont;
            if (icf != null && glyph != '\0')
                using (var big = new Font(icf.FontFamily, Theme.S(16f), GraphicsUnit.Pixel))
                { TextRenderer.DrawText(g, glyph.ToString(), big, new Point(x, Pad), Theme.Accent, Theme.Panel, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix); x += Theme.S(24); }
            TextRenderer.DrawText(g, L.T(title), Theme.FB, new Point(x, Pad - Theme.S(1)), Theme.Strong, Theme.Panel, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            bool done = (DateTime.UtcNow - copied).TotalSeconds < 2;
            string ct = L.T(done ? "Copied" : "Copy");
            int cw = TextRenderer.MeasureText(ct, Theme.FS, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.S(20);
            copyR = new Rectangle(Width - Pad - cw, Pad - Theme.S(5), cw, Theme.S(24));
            Color cf = hot ? Theme.PanelHover : Theme.Panel;
            Gfx.Surface(g, copyR, cf, false);   // 3.36: same shape as every button
            TextRenderer.DrawText(g, ct, Theme.FS, copyR, done ? Theme.Ok : Theme.Text, cf, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (keys && Focused) Gfx.FocusRing(g, new RectangleF(copyR.X + 1, copyR.Y + 1, copyR.Width - 2, copyR.Height - 2), 0);
            int y = Pad + Theme.S(28), vw = Width - Pad * 2 - KeyW;
            for (int i = 0; i < rows.Count && i < rowH.Count; i++)
            {
                TextRenderer.DrawText(g, rows[i].Key, Theme.F, new Rectangle(Pad, y, KeyW - Theme.S(8), rowH[i]), Theme.Muted, Theme.Panel, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, rows[i].Value, Theme.F, new Rectangle(Pad + KeyW, y, vw, rowH[i]), Theme.Text, Theme.Panel, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                y += rowH[i];
            }
        }
        void Copy()
        {
            try { Clipboard.SetText(CopyText); copied = DateTime.UtcNow; } catch { }
            Invalidate();
            var t = new Timer { Interval = 2100 }; t.Tick += delegate { t.Stop(); t.Dispose(); if (!IsDisposed) Invalidate(); }; t.Start();
        }
        protected override void OnMouseMove(MouseEventArgs e) { bool h = copyR.Contains(e.Location); if (h != hot) { hot = h; Cursor = h ? Cursors.Hand : Cursors.Default; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { if (e.Button == MouseButtons.Left && copyR.Contains(e.Location)) Copy(); base.OnMouseClick(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { keys = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            keys = true;
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || (e.Control && e.KeyCode == Keys.C)) { Copy(); e.Handled = true; }
            Invalidate(); base.OnKeyDown(e);
        }

        // SysInfo.Gather() writes blocks split by a blank line, each line "name (22 wide) value". One card per block.
        static readonly string[] Titles = { "Windows", "Processor, board and memory", "Graphics and gaming state", "Storage", "Network" };
        static readonly char[] Glyphs = { '', '', '', '', '' };
        public static List<KvCard> Parse(string text)
        {
            var cards = new List<KvCard>(); var rows = new List<KeyValuePair<string, string>>();
            Action flush = () =>
            {
                if (rows.Count == 0) return;
                int i = cards.Count;
                cards.Add(new KvCard(i < Titles.Length ? Titles[i] : "More", i < Glyphs.Length ? Glyphs[i] : '\0', rows));
                rows = new List<KeyValuePair<string, string>>();
            };
            foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                if (raw.Trim().Length == 0) { flush(); continue; }
                int cut = 22;
                if (raw.Length > 22 && raw[21] != ' ') { int g = raw.IndexOf("  ", 21, StringComparison.Ordinal); cut = g < 0 ? raw.Length : g; }   // a name longer than its column
                string k = raw.Length > cut ? raw.Substring(0, cut).Trim() : raw.Trim(), v = raw.Length > cut ? raw.Substring(cut).Trim() : "";
                rows.Add(new KeyValuePair<string, string>(k, v));
            }
            flush();
            return cards;
        }
    }
}
