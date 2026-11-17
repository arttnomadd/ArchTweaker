// ArchTweaker: Search box with the magnifier glyph.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArchTweaker
{
    // Search box with a Carbon frame and a magnifier glyph; the borderless TextBox inside does the typing.
    public class SearchField : Panel
    {
        public readonly TextBox Box;
        bool hover;
        public SearchField(TextBox box, int width)
        {
            Box = box; BackColor = Theme.Panel; Size = new Size(width, 30); Cursor = Cursors.IBeam;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            box.BorderStyle = BorderStyle.None; box.BackColor = Theme.Panel; box.ForeColor = Theme.Text;
            Controls.Add(box);
            Resize += delegate { Place(); }; box.FontChanged += delegate { Place(); };
            box.GotFocus += delegate { Invalidate(); }; box.LostFocus += delegate { Invalidate(); };
            EventHandler on = delegate { if (!hover) { hover = true; Invalidate(); } };
            EventHandler off = delegate { if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) { hover = false; Invalidate(); } };
            MouseEnter += on; box.MouseEnter += on; MouseLeave += off; box.MouseLeave += off;   // the box covers most of the panel
            Click += delegate { box.Focus(); };
            Place();
        }
        void Place()
        {
            int x = Height;   // icon cell is square
            Box.Location = new Point(x, Math.Max(0, (Height - Box.Height) / 2));
            Box.Width = Math.Max(20, Width - x - Theme.S(8));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Theme.Panel);
            float u = Height / 30f, cx = Height / 2f - 1 * u, cy = Height / 2f - 1 * u;
            using (var pen = new Pen(Box.Focused ? Theme.Text : Theme.Muted, 1.6f * u))
            {
                g.DrawEllipse(pen, cx - 5 * u, cy - 5 * u, 10 * u, 10 * u);
                g.DrawLine(pen, cx + 3.6f * u, cy + 3.6f * u, cx + 7.5f * u, cy + 7.5f * u);
            }
            using (var p = Gfx.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(Theme.Radius)))
            using (var pen = new Pen(Box.Focused ? Theme.Accent : hover ? Theme.Muted : Theme.Border, Box.Focused ? 2f : 1f)) g.DrawPath(pen, p);
        }
    }
}
