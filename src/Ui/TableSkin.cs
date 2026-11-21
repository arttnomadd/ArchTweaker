// ArchTweaker: Themed table rows for every list, empty state, tooltips and the sidebar footer.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ================= TABLES =================
    // Every Details ListView: 30 px rows, hover, the selection colour, a themed tick box, status columns as pills,
    // number columns right-aligned in the monospace face. Rows are drawn here; the ListView keeps hit-testing,
    // keyboard, check toggling and accessibility.
    public static class Table
    {
        // header text → kind; pages need no change. A pill column takes the row's warning colour.
        static readonly HashSet<string> PillCols = new HashSet<string> { "Status", "Verdict", "Change", "Effect", "Hidden" };
        static readonly HashSet<string> NumCols = new HashSet<string> { "Worst DPC", "Worst ISR", "DPCs", "ISRs", "Time in DPC/ISR", "≥ 500 µs", "≥ 1 ms", "Size" };
        static readonly HashSet<string> OkWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "OK", "Good", "Running", "Fine", "Low", "Yes", "Enabled", "Added" };
        static readonly HashSet<ListView> done = new HashSet<ListView>();
        static readonly Dictionary<ListView, int> hover = new Dictionary<ListView, int>();
        public static int RowHeight { get { return Theme.S(30); } }

        static bool IsPill(ColumnHeader c) { return PillCols.Contains(c.Text) || (c.Tag as string) == "pill"; }
        static bool IsNum(ColumnHeader c) { return NumCols.Contains(c.Text) || (c.Tag as string) == "num"; }

        public static void Attach(ListView lv)
        {
            if (!done.Add(lv)) return;
            lv.Disposed += delegate { done.Remove(lv); hover.Remove(lv); };
            lv.OwnerDraw = true; lv.FullRowSelect = true; lv.GridLines = false;
            // row height comes from the small image size; a 1 px image keeps the text where it was
            if (lv.SmallImageList == null) lv.SmallImageList = new ImageList { ImageSize = new Size(1, RowHeight), ColorDepth = ColorDepth.Depth8Bit };
            // 3.23.1: never change a column inside WM_CREATE: on some Windows builds LVM_SETCOLUMN fails there
            // ("ColumnInfo cannot be set") and the exception breaks the list's creation, so it stays white.
            Action align = () =>
            {
                if (lv.IsDisposed) return;
                foreach (ColumnHeader c in lv.Columns)
                    try { if (IsNum(c) && c.TextAlign != HorizontalAlignment.Right && c.Index > 0) c.TextAlign = HorizontalAlignment.Right; }
                    catch (InvalidOperationException) { }
            };
            align();
            lv.HandleCreated += delegate { try { lv.BeginInvoke(align); } catch (InvalidOperationException) { } };
            lv.DrawItem += (s, e) => { e.DrawDefault = false; };
            lv.DrawSubItem += (s, e) => Draw(lv, e);
            lv.MouseMove += (s, e) =>
            {
                var it = lv.GetItemAt(e.X, e.Y); int i = it == null ? -1 : it.Index, old;
                if (hover.TryGetValue(lv, out old) && old == i) return;
                hover[lv] = i; Repaint(lv, old); Repaint(lv, i);
            };
            lv.MouseLeave += delegate { int old; if (hover.TryGetValue(lv, out old)) { hover[lv] = -1; Repaint(lv, old); } };
            // selection and check changes repaint the row (the system does not, with owner drawing)
            lv.ItemSelectionChanged += (s, e) => Repaint(lv, e.ItemIndex);
            lv.ItemChecked += (s, e) => { if (e.Item != null) Repaint(lv, e.Item.Index); };
            lv.GotFocus += delegate { lv.Invalidate(); }; lv.LostFocus += delegate { lv.Invalidate(); };
        }
        static void Repaint(ListView lv, int i)
        {
            if (i < 0 || i >= lv.Items.Count || lv.IsDisposed) return;
            try { lv.Invalidate(lv.Items[i].Bounds); } catch { }
        }

        static void Draw(ListView lv, DrawListViewSubItemEventArgs e)
        {
            var g = e.Graphics; var it = e.Item; if (it == null) return;
            int h; if (!hover.TryGetValue(lv, out h)) h = -1;   // no hover yet: not row 0
            bool sel = it.Selected, hot = it.Index == h && !sel;
            Color bg = sel ? Theme.Sel : hot ? Theme.PanelHover : Theme.Panel;
            var r = e.Bounds;
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, r);
            using (var pen = new Pen(Theme.Grid)) g.DrawLine(pen, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
            var col = lv.Columns[e.ColumnIndex];
            Color rowTone = it.ForeColor;
            bool warnRow = rowTone == Theme.Bad || rowTone == Theme.Warn;
            bool rowHasPill = warnRow && lv.Columns.Cast<ColumnHeader>().Any(IsPill);
            Color ink = it.UseItemStyleForSubItems || e.ColumnIndex == 0 ? rowTone : e.SubItem.ForeColor;
            if (rowHasPill && ink == rowTone) ink = Theme.Text;   // the pill carries the warning; the rest of the row stays readable
            if (ink == Color.Empty || ink == SystemColors.WindowText) ink = Theme.Text;

            int x = r.X + Theme.S(8);
            if (e.ColumnIndex == 0)
            {
                // accent edge on the row with keyboard focus
                if (it.Focused && lv.Focused) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, r.X, r.Y, Theme.S(3), r.Height - 1);
                Rectangle ic; try { ic = lv.GetItemRect(it.Index, ItemBoundsPortion.Icon); } catch { ic = Rectangle.Empty; }
                if (lv.CheckBoxes)
                {
                    // the system's tick area is left of the icon; draw the box there so a click on it still toggles
                    int box = Theme.S(16), area = ic.Width > 0 ? Math.Max(box, ic.Left - r.Left) : Theme.S(22);
                    var br = new Rectangle(r.Left + Math.Max(Theme.S(6), (area - box) / 2 + Theme.S(2)), r.Y + (r.Height - box) / 2, box, box);   // clear of the focus edge
                    DrawTick(g, br, it.Checked, bg);
                    x = Math.Max(x, br.Right + Theme.S(8));
                }
                var img = lv.SmallImageList;
                if (img != null && img.ImageSize.Width > 1 && ic.Width > 0)
                {
                    Image im = null;
                    try { if (!string.IsNullOrEmpty(it.ImageKey) && img.Images.ContainsKey(it.ImageKey)) im = img.Images[it.ImageKey]; else if (it.ImageIndex >= 0 && it.ImageIndex < img.Images.Count) im = img.Images[it.ImageIndex]; } catch { }
                    if (im != null) g.DrawImage(im, ic.X, r.Y + (r.Height - img.ImageSize.Height) / 2, img.ImageSize.Width, img.ImageSize.Height);
                    x = Math.Max(x, ic.Right + Theme.S(6));
                }
            }
            var tr = new Rectangle(x, r.Y, r.Right - x - Theme.S(8), r.Height);
            string text = e.SubItem.Text ?? "";
            if (IsPill(col) && text.Length > 0 && e.ColumnIndex > 0)
            {
                // 3.38.4: a sub-item whose Tag is a Color sets its own pill tone (Apps risk badges)
                Color tone = e.SubItem.Tag is Color ? (Color)e.SubItem.Tag : warnRow ? rowTone : OkWords.Contains(text.Trim()) ? Theme.Ok : Theme.Muted;
                Pill(g, tr, text, tone, bg);
                return;
            }
            bool num = IsNum(col) && e.ColumnIndex > 0;
            var f = num ? Theme.Mono : it.Font != null && it.Font.Bold ? it.Font : lv.Font;   // 3.33: a bold row (a group header) stays bold
            var al = col.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : col.TextAlign == HorizontalAlignment.Center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
            TextRenderer.DrawText(g, text, f, tr, ink, bg, al | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        // rounded tag: a light tint of the tone, a tone border, text in the strong colour
        public static void Pill(Graphics g, Rectangle cell, string text, Color tone, Color under)
        {
            var f = Theme.FT;
            int w = Math.Min(cell.Width, TextRenderer.MeasureText(g, text, f, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.S(16));
            int h = Theme.S(20);
            var r = new Rectangle(cell.X, cell.Y + (cell.Height - h) / 2, w, h);
            Color fill = Gfx.Mix(under, tone, Theme.Dark ? 0.22 : 0.14);
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Gfx.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), h / 2f))
            { using (var b = new SolidBrush(fill)) g.FillPath(b, p); using (var pen = new Pen(Gfx.Mix(under, tone, 0.6))) g.DrawPath(pen, p); }
            g.SmoothingMode = old;
            TextRenderer.DrawText(g, text, f, r, Theme.Strong, fill, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        public static void DrawTick(Graphics g, Rectangle b, bool on, Color under)
        {
            using (var br = new SolidBrush(on ? Theme.Accent : under)) g.FillRectangle(br, b);
            using (var pen = new Pen(on ? Theme.Accent : Theme.Muted)) g.DrawRectangle(pen, b.X, b.Y, b.Width - 1, b.Height - 1);
            if (!on) return;
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            float u = b.Width / 16f;
            using (var pen = new Pen(Theme.OnAccent, Math.Max(1.6f, 1.8f * u)))
                g.DrawLines(pen, new[] { new PointF(b.X + 3.5f * u, b.Y + 8.2f * u), new PointF(b.X + 6.6f * u, b.Y + 11.2f * u), new PointF(b.X + 12.4f * u, b.Y + 4.8f * u) });
            g.SmoothingMode = old;
        }

        // DataGridView: pill for a "Status" column (Services). Everything else keeps Theme.DarkGrid's look.
        public static void Attach(DataGridView dg)
        {
            dg.RowTemplate.Height = 30;
            dg.CellPainting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var c = dg.Columns[e.ColumnIndex];
                if (c.Name != "Status" && (c.Tag as string) != "pill") return;
                string t = Convert.ToString(e.FormattedValue);
                bool sel = (e.State & DataGridViewElementStates.Selected) != 0;
                Color bg = sel ? Theme.Sel : (e.RowIndex % 2 == 1 ? Theme.AltRow : Theme.Panel);
                using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.CellBounds);
                using (var pen = new Pen(Theme.Grid)) e.Graphics.DrawLine(pen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
                if (t.Length > 0) Pill(e.Graphics, new Rectangle(e.CellBounds.X + Theme.S(6), e.CellBounds.Y, e.CellBounds.Width - Theme.S(10), e.CellBounds.Height), t, OkWords.Contains(t.Trim()) ? Theme.Ok : Theme.Muted, bg);
                e.Handled = true;
            };
        }
    }

    // ================= EMPTY STATE =================
    // One look for "nothing here": a glyph, a short title and one sentence. While data loads, grey placeholder
    // rows (a skeleton) keep the list's shape. Text comes from the page; the first sentence becomes the title.
    public class EmptyState : Control
    {
        string title = "", body = ""; bool loading;
        public EmptyState()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel; Dock = DockStyle.Fill; AccessibleRole = AccessibleRole.StaticText;
        }
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified) { }
        // "Loading…", "Reading counters…": a short text that ends with an ellipsis
        public static bool IsLoadingText(string t) { if (t == null) return false; t = t.Trim(); return t.EndsWith("…") && t.Length < 40 && t.IndexOf(". ", StringComparison.Ordinal) < 0; }
        public static void Split(string tr, out string title, out string body)
        {
            int cut = -1;
            for (int i = 0; i < tr.Length - 1; i++) if ((tr[i] == '.' || tr[i] == '!' || tr[i] == '?') && tr[i + 1] == ' ') { cut = i + 1; break; }
            if (cut > 0 && cut < tr.Length - 1) { title = tr.Substring(0, cut).Trim(); body = tr.Substring(cut).Trim(); }
            else { title = tr.Trim(); body = ""; }
        }
        public void SetText(string text)
        {
            text = text ?? "";
            loading = IsLoadingText(text);
            string tr = L.T(text);
            AccessibleName = tr;
            if (loading) { title = tr; body = ""; } else Split(tr, out title, out body);
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            if (loading) { Skeleton(g); return; }
            int w = Math.Max(Theme.S(120), Math.Min(Width - Theme.S(48), Theme.S(520))), x = (Width - w) / 2;
            int th = TextRenderer.MeasureText(g, title, Theme.FB, new Size(w, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            int bh = body.Length == 0 ? 0 : TextRenderer.MeasureText(g, body, Theme.F, new Size(w, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            int gh = Theme.S(32), total = gh + Theme.S(12) + th + (bh > 0 ? Theme.S(6) + bh : 0);
            int y = Math.Max(Theme.S(16), (Height - total) / 2 - Theme.S(12));
            // glyph in a soft circle; Segoe Fluent Icons "info", or an "i" when the font is missing
            var c = new Rectangle(Width / 2 - gh / 2, y, gh, gh);
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Gfx.Mix(BackColor, Theme.Accent, 0.14))) g.FillEllipse(b, c);
            g.SmoothingMode = old;
            var icf = NavItem.IconFont;
            if (icf != null) using (var f = new Font(icf.FontFamily, Theme.S(16f), GraphicsUnit.Pixel))
                TextRenderer.DrawText(g, "", f, c, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            else TextRenderer.DrawText(g, "i", Theme.Semi(12f), c, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            y += gh + Theme.S(12);
            TextRenderer.DrawText(g, title, Theme.FB, new Rectangle(x, y, w, th), Theme.Strong, TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            if (bh > 0) TextRenderer.DrawText(g, body, Theme.F, new Rectangle(x, y + th + Theme.S(6), w, bh), Theme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }
        // placeholder rows: bars of different widths, so the list keeps its shape while it fills
        void Skeleton(Graphics g)
        {
            int rh = Table.RowHeight, y = Theme.S(8), i = 0;
            Color bar = Gfx.Mix(Theme.Panel, Theme.Muted, Theme.Dark ? 0.18 : 0.12);
            using (var b = new SolidBrush(bar))
                while (y + rh < Height - Theme.S(28) && i < 14)
                {
                    float w1 = Width * (0.22f + 0.06f * ((i * 7) % 5)), w2 = Width * (0.10f + 0.04f * ((i * 3) % 4));
                    using (var p = Gfx.Round(new RectangleF(Theme.S(12), y + rh / 2 - Theme.S(5), w1, Theme.S(10)), Theme.S(5))) g.FillPath(b, p);
                    using (var p = Gfx.Round(new RectangleF(Width * 0.55f, y + rh / 2 - Theme.S(5), w2, Theme.S(10)), Theme.S(5))) g.FillPath(b, p);
                    y += rh; i++;
                }
            TextRenderer.DrawText(g, title, Theme.FS, new Rectangle(0, Height - Theme.S(28), Width - Theme.S(12), Theme.S(20)), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
        }
    }

    // ================= TOOLTIPS =================
    // Owner-drawn tooltips in the scheme colours (Windows draws a light box, also on dark schemes).
    public static class Tips
    {
        public static ToolTip New() { var t = new ToolTip(); Style(t); return t; }
        public static ToolTip New(ToolTip t) { Style(t); return t; }
        public static void Style(ToolTip t)
        {
            if (t == null || t.OwnerDraw) return;
            t.OwnerDraw = true; t.BackColor = Theme.Panel; t.ForeColor = Theme.Text;
            t.Popup += (s, e) =>
            {
                string text = t.GetToolTip(e.AssociatedControl) ?? "";
                var sz = TextRenderer.MeasureText(text, Theme.FS, new Size(Theme.S(420), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                e.ToolTipSize = new Size(sz.Width + Theme.S(20), sz.Height + Theme.S(12));
            };
            t.Draw += (s, e) =>
            {
                var g = e.Graphics;
                using (var b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, e.Bounds);
                using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                var r = new Rectangle(Theme.S(10), Theme.S(6), e.Bounds.Width - Theme.S(20), e.Bounds.Height - Theme.S(12));
                TextRenderer.DrawText(g, e.ToolTipText, Theme.FS, r, Theme.Text, Theme.Panel, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            };
        }
    }

    // ================= SIDEBAR FOOTER =================
    // Version, and an amber "Restart needed" badge on every page while a restart is pending. Click the badge to see
    // the restart notice again.
    public class SideFooter : Control
    {
        readonly Func<bool> pending; readonly Action show; bool hot;
        public SideFooter(Func<bool> restartPending, Action showRestart)
        {
            pending = restartPending; show = showRestart;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Dock = DockStyle.Bottom; Height = 34; BackColor = Theme.Side; TabStop = false; AccessibleRole = AccessibleRole.StatusBar;
        }
        public void Refresh2()
        {
            bool p = pending();
            TabStop = p; Cursor = p ? Cursors.Hand : Cursors.Default;
            AccessibleName = "ArchTweaker " + Program.Version + (p ? ". " + L.T("Restart needed") : "");
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, 0, 0, Width, 0);
            bool p = pending(), slim = Width < Theme.S(120);
            int x = Theme.S(slim ? 0 : 16);
            if (p)
            {
                int d = Theme.S(8), cy = Height / 2;
                var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Theme.Warn)) g.FillEllipse(b, slim ? (Width - d) / 2 : x, cy - d / 2, d, d);
                g.SmoothingMode = old;
                if (!slim)
                {
                    var r = new Rectangle(x + d + Theme.S(8), 0, Width - x - d - Theme.S(16), Height);
                    TextRenderer.DrawText(g, L.T("Restart needed"), hot ? Theme.Semi(9f) : Theme.FS, r, hot ? Theme.Strong : Theme.Text, BackColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }
            else if (!slim)
                TextRenderer.DrawText(g, "ArchTweaker " + Program.Version, Theme.FS, new Rectangle(x, 0, Width - x - Theme.S(8), Height), Theme.Muted, BackColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) Gfx.FocusRing(g, new RectangleF(2, 2, Width - 5, Height - 5), 0);
        }
        protected override void OnMouseEnter(EventArgs e) { hot = pending(); Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { if (pending()) show(); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && pending()) { show(); e.Handled = true; } base.OnKeyDown(e); }
    }
}
