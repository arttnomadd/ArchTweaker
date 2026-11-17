// ArchTweaker: Apps (UWP) page.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ================= APPS =================
    public class AppsPage : Panel, IPage
    {
        ListView lv; List<AppPkg> data; CheckBox deprov;
        public AppsPage()
        {
            BackColor = Theme.Bg;
            lv = new ListView { Dock = DockStyle.Fill, CheckBoxes = true };
            Theme.DarkList(lv);
            lv.Columns.Add("App", 260); lv.Columns.Add("Package", 330); lv.Columns.Add("Version", 120);
            var bar = Theme.Bar();
            bar.Controls.Add(Theme.Btn("Check known bloat", delegate { foreach (ListViewItem i in lv.Items) { var a = (AppPkg)i.Tag; i.Checked = a.Bloat && !a.Friendly.Contains("keep") && !a.Friendly.Contains("needed"); } }));
            bar.Controls.Add(Theme.Btn("Uncheck all", delegate { foreach (ListViewItem i in lv.Items) i.Checked = false; }));
            var rm = Theme.Lead("Remove checked", delegate { Remove(); }); bar.Controls.Add(rm);
            deprov = new TickBox { Text = "Also remove for new users", Checked = true, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(8, 6, 0, 0) };
            bar.Controls.Add(deprov);
            bar.Controls.Add(Theme.Btn("Refresh", delegate { data = null; OnShow(); }));
            bar.Controls.Add(Theme.Btn("App settings (Discord, Firefox, Spotify)…", delegate { using (var d = new AppSettingsDialog()) d.ShowDialog(FindForm()); }));   // 3.30
            Controls.Add(lv); Controls.Add(Theme.Note("You can reinstall removed apps from the Microsoft Store. 'Check known bloat' leaves items marked 'needed' or 'keep' unchecked.")); Controls.Add(bar);
        }
        public void OnShow() { if (data != null) { Fill(""); return; } Skin.Loading(lv); MainForm.Instance.Busy("Reading installed apps…", () => { data = AppsModule.Load(); foreach (var a in data) a.Icon = AppIcons.FromPackage(a.InstallLocation); }, () => Fill("")); }
        public void Filter(string q) { Fill(q); }
        void Fill(string q)
        {
            if (data == null) return;
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var a in data)
            {
                if (q.Length > 0 && (a.Name + a.Friendly).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var it = new ListViewItem(new[] { a.Friendly.Length > 0 ? a.Friendly : a.Name, a.Name, a.Version }) { Tag = a };
                AppIcons.Attach(lv, it, "pkg|" + a.Name, a.Icon);   // 3.3: the app's own logo
                if (a.Bloat) it.ForeColor = Theme.Warn;
                lv.Items.Add(it);
            }
            Skin.Loaded(lv, q.Length > 0 ? "No apps match \"" + q + "\"." : "No removable Store apps found.");
            lv.EndUpdate();
        }
        void Remove()
        {
            var sel = lv.CheckedItems.Cast<ListViewItem>().Select(i => (AppPkg)i.Tag).ToList();
            if (sel.Count == 0) return;
            string napps = sel.Count + (sel.Count == 1 ? " app" : " apps");
            if (!Dlg.Confirm("Remove " + napps + "?", "Undo can't bring removed apps back. Reinstall them from the Microsoft Store if you need them again.", "Remove " + napps, "Cancel", true, string.Join("\n", sel.Select(a => "• " + (a.Friendly.Length > 0 ? a.Friendly : a.Name)).ToArray()))) return;
            bool d = deprov.Checked;
            MainForm.Instance.Busy("Removing apps…", () => { AppsModule.Remove(sel, d); data = AppsModule.Load(); foreach (var a in data) a.Icon = AppIcons.FromPackage(a.InstallLocation); }, () => Fill(""));
        }
    }
}
