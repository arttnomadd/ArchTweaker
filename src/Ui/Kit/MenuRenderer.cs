// ArchTweaker: Carbon-style renderer for context menus and drop-down menus.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ---- Carbon menus (2.9): ContextMenuStrip / ToolStrip drawn with the theme tokens
    public class CarbonMenuRenderer : ToolStripProfessionalRenderer
    {
        public CarbonMenuRenderer() : base(new Colors()) { RoundedEdges = false; }
        class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Panel; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Panel; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Panel; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Panel; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color MenuItemBorder { get { return Theme.PanelHover; } }
            public override Color MenuItemSelected { get { return Theme.PanelHover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.PanelHover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.PanelHover; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Panel; } }
            public override Color CheckBackground { get { return Theme.Sel; } }
            public override Color CheckSelectedBackground { get { return Theme.Sel; } }
            public override Color CheckPressedBackground { get { return Theme.Sel; } }
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted;
            var mi = e.Item as ToolStripMenuItem;   // 3.10.4: the right-hand note ("default", a shortcut) is muted
            if (mi != null && !string.IsNullOrEmpty(mi.ShortcutKeyDisplayString) && e.Text == mi.ShortcutKeyDisplayString) e.TextColor = Theme.Muted;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            if (e.Item != null && "swatch".Equals(e.Item.Tag)) return;   // 3.10.4: the tick is part of the swatch image
            var r = e.ImageRectangle; r.Inflate(-1, -1);
            using (var b = new SolidBrush(Theme.Sel)) e.Graphics.FillRectangle(b, r);
            var g = e.Graphics; var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Strong, 1.8f * Theme.Scale))
                g.DrawLines(pen, new[] { new PointF(r.X + r.Width * 0.22f, r.Y + r.Height * 0.52f), new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.72f), new PointF(r.X + r.Width * 0.78f, r.Y + r.Height * 0.3f) });
            g.SmoothingMode = old;
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Theme.Muted; base.OnRenderArrow(e); }
        // 3.10.4: swatch items carry their own tick in the image: draw the image only, without the checked box
        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (e.Item != null && "swatch".Equals(e.Item.Tag) && e.Image != null) { e.Graphics.DrawImage(e.Image, e.ImageRectangle.X, e.ImageRectangle.Y + (e.ImageRectangle.Height - e.Image.Height) / 2, e.Image.Width, e.Image.Height); return; }
            base.OnRenderItemImage(e);
        }
    }
}
