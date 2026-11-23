// ArchTweaker: Disk space page: treemap view and the cleanup preview.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ===================== DISK SPACE =====================
    public class DiskPage : Panel, IPage
    {
        readonly TreemapView map = new TreemapView { Dock = DockStyle.Fill };
        readonly ComboBox drive = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        readonly Label path;
        readonly Button scanBtn;
        volatile bool cancel; bool scanning;
        public DiskPage()
        {
            BackColor = Theme.Bg;
            drive.BackColor = Theme.Panel; drive.ForeColor = Theme.Text; drive.FlatStyle = FlatStyle.Flat; drive.Margin = new Padding(0, 4, 8, 0); drive.AccessibleName = "Drive";
            drive.Tag = "notr";
            try { foreach (var d in DriveInfo.GetDrives()) { try { if (d.IsReady && d.DriveType == DriveType.Fixed) drive.Items.Add(d.Name); } catch { } } } catch { }
            if (drive.Items.Count > 0) drive.SelectedIndex = 0;
            var bar = Theme.Bar();
            bar.Controls.Add(drive);
            scanBtn = Theme.Lead("Scan drive", delegate { if (scanning) cancel = true; else Scan(); });
            bar.Controls.Add(scanBtn);
            bar.Controls.Add(Theme.Btn("Up one level", delegate { map.Up(); }));
            bar.Controls.Add(Theme.Btn("Open in Explorer", delegate { if (map.Current != null) Shell.OpenVisible("explorer.exe", "\"" + map.Current.Path + "\""); }));
            path = new Label { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.FS, Margin = new Padding(12, 9, 0, 0), UseMnemonic = false, Tag = "notr" };
            bar.Controls.Add(path);
            map.Navigated += delegate { path.Text = map.Current == null ? "" : map.Current.Path + "  ·  " + CleanupModule.Fmt(map.Current.Size); };
            Controls.Add(map);
            Controls.Add(Theme.Note("Read-only map of what uses space. Bigger box, bigger folder. Click a box to go into it. Right-click to open it in Explorer. This page deletes nothing."));
            Controls.Add(bar);
            Disposed += delegate { cancel = true; };
        }
        public void OnShow() { }
        public void Filter(string q) { }
        void Scan()
        {
            if (drive.SelectedItem == null) return;
            string root = drive.SelectedItem.ToString(); DirNode res = null;
            scanning = true; cancel = false; scanBtn.Text = L.T("Stop scan");
            var started = DateTime.Now;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    res = DiskScan.Scan(root, () => cancel, (n, at) =>
                    {
                        try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)delegate { path.Text = L.T("Scanning…") + " " + n.ToString("N0") + " " + L.T("files") + " — " + at; }); } catch { }
                    });
                }
                catch (Exception ex) { Log.W("Disk scan: {0}", ex.Message); }
                try
                {
                    if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)delegate
                    {
                        scanning = false; scanBtn.Text = L.T("Scan drive");
                        if (res != null) map.SetRoot(res);
                        Log.W("Disk scan of {0}: {1} in {2:0} s{3}", root, res == null ? "?" : CleanupModule.Fmt(res.Size), (DateTime.Now - started).TotalSeconds, cancel ? " (stopped)" : "");
                    });
                }
                catch { }
            });
        }
    }

    public class TreemapView : Control
    {
        DirNode cur; List<KeyValuePair<DirNode, RectangleF>> cells = new List<KeyValuePair<DirNode, RectangleF>>();
        DirNode hover; readonly ToolTip tip = Tips.New();
        public event EventHandler Navigated;
        public DirNode Current { get { return cur; } }
        static Color[] HcHues { get { return new[] { SystemColors.Highlight, SystemColors.HotTrack, SystemColors.WindowText, SystemColors.GrayText }; } }
        static readonly Color[] Hues = { Color.FromArgb(225, 29, 46), Color.FromArgb(47, 111, 235), Color.FromArgb(20, 184, 166), Color.FromArgb(255, 176, 32), Color.FromArgb(139, 92, 246), Color.FromArgb(52, 199, 89) };
        public TreemapView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            AccessibleRole = AccessibleRole.Diagram; AccessibleName = "Disk space map"; TabStop = true;
            Disposed += delegate { tip.Dispose(); };
        }
        public void SetRoot(DirNode n) { Go(n); }
        public void Up() { if (cur != null && cur.Parent != null) Go(cur.Parent); }
        void Go(DirNode n)
        {
            cur = n; Relayout(); Invalidate();
            AccessibleDescription = n == null ? "" : string.Join(", ", n.Kids.Take(10).Select(k => k.Name + " " + CleanupModule.Fmt(k.Size)).ToArray());
            var h = Navigated; if (h != null) h(this, EventArgs.Empty);
        }
        void Relayout() { cells = cur == null ? new List<KeyValuePair<DirNode, RectangleF>>() : Treemap.Layout(cur.Kids, new RectangleF(1, 1, Width - 2, Height - 2)); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }
        DirNode At(Point p) { foreach (var c in cells) if (c.Value.Contains(p)) return c.Key; return null; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            var n = At(e.Location);
            if (n != hover) { hover = n; Invalidate(); tip.SetToolTip(this, n == null ? "" : (n.IsFiles ? L.T("Files in") + " " + n.Path : n.Path) + "\n" + CleanupModule.Fmt(n.Size)); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = null; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            Focus();
            var n = At(e.Location); if (n == null) return;
            if (e.Button == MouseButtons.Right) { Shell.OpenVisible("explorer.exe", "\"" + n.Path + "\""); return; }
            if (!n.IsFiles && n.Kids.Count > 0) Go(n);
            base.OnMouseClick(e);
        }
        protected override bool IsInputKey(Keys k) { return k == Keys.Back || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Back) { Up(); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Panel);
            if (cur == null) { TextRenderer.DrawText(g, L.T("Pick a drive and press Scan drive."), Theme.F, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); return; }
            int i = 0;
            foreach (var c in cells)
            {
                var hues = SystemInformation.HighContrast ? HcHues : Hues;   // 3.40: High Contrast uses the system colours
                var r = c.Value; Color baseC = c.Key.IsFiles ? Theme.Muted : hues[i++ % hues.Length];
                Color fill = Gfx.Mix(Theme.Panel, baseC, c.Key == hover ? 0.75 : 0.5);
                using (var b = new SolidBrush(fill)) g.FillRectangle(b, r);
                using (var pen = new Pen(Theme.Panel, 2)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                if (r.Width > 60 && r.Height > 34)
                {
                    var tr = Rectangle.Round(new RectangleF(r.X + 6, r.Y + 4, r.Width - 12, r.Height - 8));
                    Color ink = Theme.InkOn(fill);
                    TextRenderer.DrawText(g, (c.Key.IsFiles ? L.T("Files in this folder") : c.Key.Name) + "\n" + CleanupModule.Fmt(c.Key.Size), Theme.FS, tr, ink, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }
            if (Focused && ShowFocusCues) Gfx.FocusRing(g, new RectangleF(1, 1, Width - 3, Height - 3), Theme.S(Theme.Radius));
        }
    }

    // ===================== CLEANUP PREVIEW =====================
    public class CleanPreview : Form
    {
        readonly ListView lv = new ListView { Dock = DockStyle.Fill, CheckBoxes = true };
        public static void Open(IWin32Window owner, List<CleanTarget> targets)
        {
            using (var f = new CleanPreview(targets)) f.ShowDialog(owner);
        }
        CleanPreview(List<CleanTarget> targets)
        {
            Text = L.T("Preview: what Cleanup will clean"); BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F;
            StartPosition = FormStartPosition.CenterParent; Size = new Size(Theme.S(820), Theme.S(560)); MinimizeBox = false; ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None; Chrome.Hook(this);
            Theme.DarkList(lv);
            lv.Columns.Add("Folder or file", Theme.S(470)); lv.Columns.Add("Size", Theme.S(100)); lv.Columns.Add("Item", Theme.S(200));
            var bar = Theme.Bar(); bar.Dock = DockStyle.Bottom;
            bar.Controls.Add(Theme.Lead("Save exclusions", delegate { Save(); }));
            bar.Controls.Add(Theme.Btn("Clear all exclusions", delegate { CleanExclude.Save(new string[0]); foreach (ListViewItem it in lv.Items) it.Checked = true; }));
            bar.Controls.Add(Theme.Btn("Close", delegate { Close(); }));
            var note = Theme.Note("The biggest folders and files in each ticked item. Untick anything you want to keep. Cleanup then skips it from now on, also in the weekly cleanup.");
            Controls.Add(lv); Controls.Add(note); Controls.Add(bar);
            Skin.Loading(lv);
            var cur = CleanExclude.All.ToList();
            var rows = new List<Tuple<string, long, string>>();
            Shown += delegate
            {
                Cursor = Cursors.WaitCursor;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    foreach (var t in targets.Where(x => x.Dirs != null))
                    {
                        IEnumerable<string> dirs; try { dirs = t.Dirs().ToList(); } catch { continue; }
                        foreach (var d in dirs)
                        {
                            if (!Directory.Exists(d)) continue;
                            try
                            {
                                if (t.Pattern == "*") foreach (var sd in Directory.GetDirectories(d)) rows.Add(Tuple.Create(sd, DirSize(sd), t.Title));
                                foreach (var f in Directory.GetFiles(d, t.Pattern)) { long len = 0; try { len = new FileInfo(f).Length; } catch { } rows.Add(Tuple.Create(f, len, t.Title)); }
                            }
                            catch { }
                        }
                    }
                    foreach (var e in cur) if (!rows.Any(r => r.Item1.Equals(e, StringComparison.OrdinalIgnoreCase))) rows.Add(Tuple.Create(e, 0L, "(excluded)"));
                    try
                    {
                        if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)delegate
                        {
                            lv.BeginUpdate();
                            foreach (var r in rows.OrderByDescending(x => x.Item2).Take(300))
                                lv.Items.Add(new ListViewItem(new[] { r.Item1, CleanupModule.Fmt(r.Item2), L.T(r.Item3) }) { Checked = !cur.Contains(r.Item1, StringComparer.OrdinalIgnoreCase), Tag = r.Item1 });
                            lv.EndUpdate(); Skin.Loaded(lv, "These items are empty.");
                            Cursor = Cursors.Default;
                        });
                    }
                    catch { }
                });
            };
            L.Apply(this);
        }
        static long DirSize(string d)
        {
            long s = 0;
            try { foreach (var f in new DirectoryInfo(d).EnumerateFiles("*", SearchOption.AllDirectories)) { try { s += f.Length; } catch { } } } catch { }
            return s;
        }
        void Save()
        {
            var listed = lv.Items.Cast<ListViewItem>().Select(i => (string)i.Tag).ToList();
            var keep = CleanExclude.All.Where(e => !listed.Contains(e, StringComparer.OrdinalIgnoreCase)).ToList();
            keep.AddRange(lv.Items.Cast<ListViewItem>().Where(i => !i.Checked).Select(i => (string)i.Tag));
            CleanExclude.Save(keep);
            AppShell.Current.Notify(keep.Count == 0 ? "No exclusions: Cleanup cleans everything in the list." : keep.Count + (keep.Count == 1 ? " item excluded from cleaning." : " items excluded from cleaning."), Toast.Kind.Ok);
            Close();
        }
    }
}
