// ArchTweaker: What changed page: snapshot diff between two points in time.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ===================== WHAT CHANGED =====================
    public class ChangesPage : Panel, IPage
    {
        readonly ListView lv = new ListView { Dock = DockStyle.Fill, CheckBoxes = true };
        readonly Label info;
        List<SnapDiff> diffs = new List<SnapDiff>();
        public ChangesPage()
        {
            BackColor = Theme.Bg;
            Theme.DarkList(lv);
            lv.Columns.Add("Change", 110); lv.Columns.Add("Name", 330); lv.Columns.Add("At last apply", 170); lv.Columns.Add("Now", 260);
            var bar = Theme.Bar();
            bar.Controls.Add(Theme.Lead("Fix ticked", delegate { Fix(); }));
            bar.Controls.Add(Theme.Btn("Check again", delegate { Load2(); }));
            bar.Controls.Add(Theme.Btn("Accept current state", delegate { Snapshot.Capture(); Load2(); }));
            info = new Label { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.FS, Margin = new Padding(12, 9, 0, 0), UseMnemonic = false };
            bar.Controls.Add(info);
            lv.ItemCheck += (s, e) => { var d = lv.Items[e.Index].Tag as SnapDiff; if (d != null && e.NewValue == CheckState.Checked && !d.CanFix) e.NewValue = CheckState.Unchecked; };
            Controls.Add(lv);
            Controls.Add(Theme.Note("Compares this PC with the moment of your last apply. It shows tweaks that Windows or another program reset, services whose start type changed, and programs added to or removed from startup. Fix puts tweaks and services back. Startup entries are for your information only. Manage them on the Startup page."));
            Controls.Add(bar);
        }
        public void OnShow() { Load2(); }
        public void Filter(string q) { }
        void Load2()
        {
            Skin.Loading(lv);
            AppShell.Current.Busy("Comparing with the last apply…", delegate { diffs = Snapshot.Diff(); }, delegate
            {
                lv.BeginUpdate(); lv.Items.Clear();
                foreach (var d in diffs)
                {
                    string now = d.After.StartsWith("Added: ") ? L.T("Added:") + " " + d.After.Substring(7) : L.T(d.After);
                    var it = new ListViewItem(new[] { L.T(d.Kind), d.Kind == "Tweak" ? L.T(d.Name) : d.Name, L.T(d.Before), now }) { Tag = d, Checked = d.CanFix };
                    it.ForeColor = d.CanFix ? Theme.Warn : Theme.Text;
                    lv.Items.Add(it);
                }
                lv.EndUpdate();
                Skin.Loaded(lv, Snapshot.Exists ? "Nothing changed since your last apply." : "No snapshot yet. ArchTweaker saves one each time you apply tweaks.");
                info.Text = Snapshot.Exists ? "Last apply: " + Snapshot.When.ToString("yyyy-MM-dd HH:mm") : "";
            });
        }
        void Fix()
        {
            var sel = lv.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (SnapDiff)i.Tag).Where(d => d.CanFix).ToList();
            if (sel.Count == 0) { AppShell.Current.Notify("Nothing ticked.", Toast.Kind.Warn); return; }
            int ok = 0;
            AppShell.Current.Busy("Fixing…", delegate { foreach (var d in sel) if (Snapshot.Fix(d)) ok++; Native.BroadcastSettingChange(); }, delegate
            {
                AppShell.Current.Notify(ok + (ok == 1 ? " change put back." : " changes put back."), ok == sel.Count ? Toast.Kind.Ok : Toast.Kind.Warn);
                Load2();
            });
        }
    }
}
