// ArchTweaker: Drivers page: driver store packages and the slow-driver scan.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ===================== DRIVERS =====================
    public class DriversPage : Panel, IPage
    {
        readonly ListView lv = new ListView { Dock = DockStyle.Fill, CheckBoxes = true };
        readonly Label total;
        List<DriverPkg> pkgs = new List<DriverPkg>();
        bool loaded;
        public DriversPage()
        {
            BackColor = Theme.Bg;
            Theme.DarkList(lv);
            lv.Columns.Add("Driver", 230); lv.Columns.Add("Class", 120); lv.Columns.Add("Provider", 160); lv.Columns.Add("Version", 130); lv.Columns.Add("Date", 95); lv.Columns.Add("Status", 170);
            var bar = Theme.Bar();
            bar.Controls.Add(Theme.Lead("Remove ticked drivers", delegate { Remove(); }));
            bar.Controls.Add(Theme.Btn("Tick all old drivers", delegate { foreach (ListViewItem it in lv.Items) it.Checked = ((DriverPkg)it.Tag).Removable; }));
            bar.Controls.Add(Theme.Btn("Rescan", delegate { Load2(); }));
            bar.Controls.Add(Theme.Btn("Ghost devices…", delegate { using (var g = new GhostDialog()) g.ShowDialog(FindForm()); }));   // 3.5
            bar.Controls.Add(Theme.Btn("NVIDIA telemetry tweak", delegate { AppShell.Current.ShowPage(Cat.Gpu); }));
            total = new Label { AutoSize = true, ForeColor = Theme.Text, Font = Theme.FB, Margin = new Padding(12, 8, 0, 0), UseMnemonic = false };
            bar.Controls.Add(total);
            lv.ItemCheck += (s, e) => { var p = lv.Items[e.Index].Tag as DriverPkg; if (p != null && e.NewValue == CheckState.Checked && !p.Removable) e.NewValue = CheckState.Unchecked; };
            Controls.Add(lv);
            Controls.Add(Theme.Note("Every driver update leaves the old package in the Windows driver store. You can only remove old versions that no device uses. ArchTweaker always keeps the newest version and drivers in use. It exports each removed package to the backup folder first, so Tools & backup → Install drivers from folder can put it back."));
            Controls.Add(bar);
        }
        public void OnShow() { if (!loaded) Load2(); }
        public void Filter(string q) { }
        void Load2()
        {
            loaded = true; Skin.Loading(lv);
            AppShell.Current.LoadData("Reading the driver store…", delegate { pkgs = DriverStore.Scan(); }, delegate
            {
                lv.BeginUpdate(); lv.Items.Clear();
                foreach (var p in pkgs)
                {
                    var it = new ListViewItem(new[] { p.Original, p.Class, p.Provider, p.Version ?? "", p.Date == DateTime.MinValue ? "" : p.Date.ToString("yyyy-MM-dd"),
                        L.T(p.InUse ? "In use" : p.Newest ? "Newest, kept" : "Old, safe to remove") }) { Tag = p };
                    it.ForeColor = p.Removable ? Theme.Text : Theme.Muted;
                    lv.Items.Add(it);
                }
                lv.EndUpdate(); Skin.Loaded(lv, "ArchTweaker found no third-party drivers.");
                int n = pkgs.Count(p => p.Removable);
                total.Text = n == 0 ? "No old drivers to remove" : (n == 1 ? L.T("1 old driver to remove") : L.F("{0} old drivers to remove", n));
            });
        }
        void Remove()
        {
            var sel = lv.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (DriverPkg)i.Tag).Where(p => p.Removable).ToList();
            if (sel.Count == 0) { AppShell.Current.Notify("Nothing ticked. Tick the old drivers to remove.", Toast.Kind.Warn); return; }
            if (!Dlg.Confirm("Remove " + sel.Count + (sel.Count == 1 ? " old driver?" : " old drivers?"), "ArchTweaker exports each package to the backup folder first. Windows refuses drivers that a device still uses.", "Remove", "Cancel", true,
                string.Join("\n", sel.Select(p => "• " + p.Original + " " + p.Version + " (" + p.Inf + ")").ToArray()))) return;
            int n = 0; string backup = null;
            AppShell.Current.Busy("Removing old drivers…", delegate { n = DriverStore.Remove(sel, out backup); }, delegate
            {
                AppShell.Current.Notify(n + (n == 1 ? " old driver removed." : " old drivers removed.") + " " + L.T("Backup:") + " " + backup, Toast.Kind.Ok);
                Load2();
            });
        }
    }
}
