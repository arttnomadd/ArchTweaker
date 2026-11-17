// ArchTweaker: small number tiles, two per row (3.33): the page summary's Applied / Waiting / Restart / Risky.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ArchTweaker
{
    public class StatTiles : Control
    {
        public class Tile { public string Label, Value; public Color Tone; }
        readonly List<Tile> tiles = new List<Tile>();
        public StatTiles()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel; AccessibleRole = AccessibleRole.Grouping;
        }
        public void Set(IEnumerable<Tile> list)
        {
            tiles.Clear(); tiles.AddRange(list);
            int rows = (tiles.Count + 1) / 2;
            Height = rows == 0 ? 0 : rows * Theme.S(58) + (rows - 1) * Theme.S(8) + Theme.S(10);
            var names = new List<string>(); foreach (var t in tiles) names.Add(t.Label + " " + t.Value);
            AccessibleName = string.Join(", ", names.ToArray());
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            int gap = Theme.S(8), w = (Width - gap) / 2, h = Theme.S(58);
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                var r = new Rectangle((i % 2) * (w + gap), (i / 2) * (h + gap), w, h);
                // 3.36: an inset read-out (a step darker than the panel it sits in), caption label, instrument number
                Color well = Gfx.Mix(Theme.Panel, Theme.Bg, 0.6);
                var old = g.SmoothingMode; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var p = Gfx.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Theme.S((float)Theme.Radius)))
                { using (var b = new SolidBrush(well)) g.FillPath(b, p); using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, p); }
                g.SmoothingMode = old;
                TextRenderer.DrawText(g, Theme.CapFit(t.Label, Theme.FT, r.Width - Theme.S(16)), Theme.FT, new Rectangle(r.X + Theme.S(12), r.Y + Theme.S(8), r.Width - Theme.S(16), Theme.S(16)), Theme.Muted, well, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, t.Value, Theme.Num(16f), new Rectangle(r.X + Theme.S(11), r.Y + Theme.S(26), r.Width - Theme.S(16), Theme.S(26)), t.Tone, well, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
