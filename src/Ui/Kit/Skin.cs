// ArchTweaker: Skin: themes every control on a page, empty states and loading states.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ---- Carbon skin for the stock WinForms controls (2.6): drop-downs, checkboxes, grids and list headers
    // follow the theme tokens instead of drawing light system chrome in dark mode.
    public static class Skin
    {
        public static void Apply(Control c)
        {
            if (c.Tag as string == "noskin") return;
            var cb = c as ComboBox; if (cb != null) { Combo(cb); return; }
            var ck = c as CheckBox; if (ck != null) { Check(ck); return; }
            var rb = c as RadioButton; if (rb != null) { rb.FlatStyle = FlatStyle.Flat; rb.FlatAppearance.BorderColor = Theme.Muted; rb.FlatAppearance.CheckedBackColor = Theme.Accent; return; }
            var dg = c as DataGridView; if (dg != null) { Grid(dg); return; }
            var lv = c as ListView; if (lv != null) { List(lv); return; }
        }

        // checkbox: flat box with a Muted (≥ 3:1) border, accent fill when ticked
        static void Check(CheckBox ck)
        {
            if (ck.Appearance == Appearance.Button) return;
            ck.FlatStyle = FlatStyle.Flat;
            ck.FlatAppearance.BorderColor = Theme.Muted;
            ck.FlatAppearance.CheckedBackColor = Theme.Accent;
            ck.FlatAppearance.MouseOverBackColor = Theme.PanelHover;
            ck.FlatAppearance.MouseDownBackColor = Theme.Grid;
            ck.Cursor = Cursors.Hand;
        }

        // drop-down: owner-drawn items + a painter for the frame and chevron (the system draws a light button otherwise)
        static void Combo(ComboBox cb)
        {
            if (cb.DropDownStyle != ComboBoxStyle.DropDownList || cb.DrawMode != DrawMode.Normal) return;
            cb.FlatStyle = FlatStyle.Flat; cb.BackColor = Theme.Panel; cb.ForeColor = Theme.Text;
            cb.DrawMode = DrawMode.OwnerDrawFixed; cb.ItemHeight = Math.Max(cb.Font.Height + Theme.S(6), Theme.S(20));
            cb.DrawItem += (s, e) =>
            {
                bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
                bool sel = !edit && (e.State & DrawItemState.Selected) != 0;
                using (var br = new SolidBrush(sel ? Theme.Sel : Theme.Panel)) e.Graphics.FillRectangle(br, e.Bounds);
                if (e.Index < 0) return;
                var r = e.Bounds; r.X += Theme.S(6); r.Width -= Theme.S(6);
                TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items[e.Index]), cb.Font, r, cb.Enabled ? Theme.Text : Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            var skin = new ComboFrame(cb);
            cb.GotFocus += delegate { cb.Invalidate(); }; cb.LostFocus += delegate { cb.Invalidate(); };
            cb.MouseEnter += delegate { skin.Hover = true; cb.Invalidate(); }; cb.MouseLeave += delegate { skin.Hover = false; cb.Invalidate(); };
        }
        class ComboFrame : NativeWindow
        {
            readonly ComboBox cb; public bool Hover; bool logged;
            public ComboFrame(ComboBox cb)
            {
                this.cb = cb;
                if (cb.IsHandleCreated) AssignHandle(cb.Handle);
                cb.HandleCreated += delegate { if (Handle != IntPtr.Zero) ReleaseHandle(); AssignHandle(cb.Handle); };
                cb.HandleDestroyed += delegate { ReleaseHandle(); };
            }
            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg != 0x000F) return;   // WM_PAINT: paint over the system frame and button
                try
                {
                    using (var g = Graphics.FromHwnd(m.HWnd))
                    {
                        int w = cb.Width, h = cb.Height, bw = Theme.S(22);
                        var btn = new Rectangle(w - bw, 1, bw - 1, h - 2);
                        using (var br = new SolidBrush(Hover && cb.Enabled ? Theme.PanelHover : Theme.Panel)) g.FillRectangle(br, btn);
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        float cx = btn.X + btn.Width / 2f, cy = h / 2f, u = Theme.Scale;
                        using (var pen = new Pen(cb.Enabled ? Theme.Muted : Theme.Border, 1.6f * u))
                            g.DrawLines(pen, new[] { new PointF(cx - 4 * u, cy - 2 * u), new PointF(cx, cy + 2 * u), new PointF(cx + 4 * u, cy - 2 * u) });
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                        using (var pen = new Pen(cb.Focused ? Theme.Accent : Hover ? Theme.Muted : Theme.Border)) g.DrawRectangle(pen, 0, 0, w - 1, h - 1);
                    }
                }
                catch (Exception ex) { if (!logged) { logged = true; Log.W("Combo skin paint: {0}", ex.Message); } }
            }
        }

        static void Grid(DataGridView dg)
        {
            dg.EnableHeadersVisualStyles = false;
            dg.BackgroundColor = Theme.Panel; dg.GridColor = Theme.Grid; dg.BorderStyle = BorderStyle.None;
            if (dg.CellBorderStyle == DataGridViewCellBorderStyle.Single) dg.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dg.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            var h = dg.ColumnHeadersDefaultCellStyle;
            h.BackColor = Theme.Side; h.ForeColor = Theme.Muted; h.SelectionBackColor = Theme.Side; h.SelectionForeColor = Theme.Muted;
            h.Font = Theme.FT; h.Padding = new Padding(Theme.S(6), 0, Theme.S(6), 0);
            if (dg.ColumnHeadersHeightSizeMode != DataGridViewColumnHeadersHeightSizeMode.AutoSize)
            { dg.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; dg.ColumnHeadersHeight = Math.Max(dg.ColumnHeadersHeight, 30); }   // 96-dpi value: ScaleTree scales it
            var d = dg.DefaultCellStyle;
            d.BackColor = Theme.Panel; d.ForeColor = Theme.Text; d.SelectionBackColor = Theme.Sel; d.SelectionForeColor = Theme.Strong;
            dg.AlternatingRowsDefaultCellStyle.BackColor = Theme.AltRow;
            dg.RowHeadersDefaultCellStyle.BackColor = Theme.Side; dg.RowHeadersDefaultCellStyle.SelectionBackColor = Theme.Sel;
        }

        // list headers: owner-drawn (Side background, Muted semibold text); rows keep the system drawing
        static void List(ListView lv)
        {
            lv.BackColor = Theme.Panel; lv.ForeColor = Theme.Text;
            if (lv.BorderStyle == BorderStyle.Fixed3D) lv.BorderStyle = BorderStyle.None;
            if (lv.View != View.Details) return;
            FillLastColumn(lv);
            if (lv.OwnerDraw) { Table.Attach(lv); return; }   // already themed (Theme.DarkList)
            lv.DrawColumnHeader += (s, e) => Theme.DrawHeader(e);
            Table.Attach(lv);   // 3.16: rows drawn in the scheme (hover, tick box, pills, tabular numbers)
        }

        // the strip right of the last column is drawn by the system in light colours: let the last column fill it
        static readonly HashSet<ListView> filled = new HashSet<ListView>();
        public static void FillLastColumn(ListView lv)
        {
            if (!filled.Add(lv)) return;
            lv.Disposed += delegate { filled.Remove(lv); };
            EventHandler fill = delegate
            {
                if (lv.Columns.Count == 0) return;
                int used = 0; for (int i = 0; i < lv.Columns.Count - 1; i++) used += lv.Columns[i].Width;
                int w = lv.ClientSize.Width - used; var last = lv.Columns[lv.Columns.Count - 1];
                if (w > Theme.S(60) && last.Width != w) last.Width = w;
            };
            lv.Resize += fill; lv.ColumnWidthChanged += (s, e) => { if (e.ColumnIndex < lv.Columns.Count - 1) fill(s, EventArgs.Empty); };
            lv.HandleCreated += fill;
        }

        // ---- loading / empty message drawn over an empty list (2.8)
        static readonly Dictionary<Control, EmptyState> overlays = new Dictionary<Control, EmptyState>();
        // text = null hides the message. Call with "Loading…" before a background load and with the empty-state text after.
        // 3.16: a short text ending in "…" shows placeholder rows; other text shows a glyph, a title and a sentence.
        public static void SetEmpty(Control list, string text)
        {
            EmptyState l;
            if (!overlays.TryGetValue(list, out l))
            {
                if (text == null) return;
                l = new EmptyState();
                list.Controls.Add(l); overlays[list] = l;
                list.Disposed += delegate { overlays.Remove(list); };
            }
            l.SetText(text ?? ""); l.Visible = text != null;
            if (l.Visible) l.BringToFront();
            list.AccessibleDescription = text;
        }
        public static void Loading(Control list) { SetEmpty(list, "Loading…"); }
        // after a load: shows `emptyText` when the list has no rows, hides the message otherwise
        public static void Loaded(ListView lv, string emptyText) { SetEmpty(lv, lv.Items.Count == 0 ? emptyText : null); }
        public static void Loaded(DataGridView g, string emptyText) { SetEmpty(g, g.Rows.Count == 0 ? emptyText : null); }
    }
}
