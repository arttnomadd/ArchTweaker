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
            lv.Columns.Add("App", 260); lv.Columns.Add(new ColumnHeader { Text = "Risk", Width = 96, Tag = "pill" }); lv.Columns.Add("Package", 300); lv.Columns.Add("Version", 110);
            lv.ShowItemToolTips = true;   // 3.38.4: the reason behind each badge
            var bar = Theme.Bar();
            bar.Controls.Add(Theme.Btn("Check known bloat", delegate { foreach (ListViewItem i in lv.Items) i.Checked = ((AppPkg)i.Tag).Risk == AppRisk.Safe; }));   // 3.38.4: Safe apps only
            bar.Controls.Add(Theme.Btn("Uncheck all", delegate { foreach (ListViewItem i in lv.Items) i.Checked = false; }));
            var rm = Theme.Lead("Remove checked", delegate { Remove(); }); bar.Controls.Add(rm);
            deprov = new TickBox { Text = "Also remove for new users", Checked = true, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(8, 6, 0, 0) };
            bar.Controls.Add(deprov);
            bar.Controls.Add(Theme.Btn("Refresh", delegate { data = null; OnShow(); }));
            bar.Controls.Add(Theme.Btn("App settings (Discord, Firefox, Spotify)…", delegate { using (var d = new AppSettingsDialog()) d.ShowDialog(FindForm()); }));   // 3.30
            Controls.Add(lv); Controls.Add(Theme.Note("Safe: nothing else needs it. Caution: a feature or game service stops working. Keep: Windows or other programs need it. Unknown: check what it is first. 'Check known bloat' ticks Safe apps only. Point at a badge to see why.")); Controls.Add(bar);
        }
        public void OnShow() { if (data != null) { Fill(""); return; } Skin.Loading(lv); MainForm.Instance.Load("Reading installed apps…", () => { data = AppsModule.Load(); foreach (var a in data) a.Icon = AppIcons.FromPackage(a.InstallLocation); }, () => Fill("")); }
        public void Filter(string q) { Fill(q); }
        void Fill(string q)
        {
            if (data == null) return;
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var a in data)
            {
                if (q.Length > 0 && (a.Name + a.Friendly).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var it = new ListViewItem(new[] { a.Friendly.Length > 0 ? a.Friendly : a.Name, RiskLabel(a.Risk), a.Name, a.Version }) { Tag = a };
                AppIcons.Attach(lv, it, "pkg|" + a.Name, a.Icon);   // 3.3: the app's own logo
                it.SubItems[1].Tag = RiskTone(a.Risk);   // 3.38.4: the badge colour (TableSkin pill)
                it.ToolTipText = RiskLabel(a.Risk) + ": " + L.T(AppsModule.WhyOf(a.Name));
                lv.Items.Add(it);
            }
            Skin.Loaded(lv, q.Length > 0 ? "No apps match \"" + q + "\"." : "No removable Store apps found.");
            lv.EndUpdate();
        }
        // 3.38.4: risk badge text and colour
        static string RiskLabel(AppRisk r) { return r == AppRisk.Safe ? L.T("Safe") : r == AppRisk.Caution ? L.T("Caution") : r == AppRisk.Keep ? L.T("Keep") : L.T("Unknown"); }
        static Color RiskTone(AppRisk r) { return r == AppRisk.Safe ? Theme.Ok : r == AppRisk.Caution ? Theme.Warn : r == AppRisk.Keep ? Theme.Bad : Theme.Muted; }
        // one line per app for the confirm dialog; Caution and Keep apps say what stops working
        internal static string ConfirmLine(AppPkg a)
        {
            string n = a.Friendly.Length > 0 ? a.Friendly : a.Name;
            return a.Risk == AppRisk.Caution || a.Risk == AppRisk.Keep ? "• " + n + " (" + RiskLabel(a.Risk) + "): " + L.T(AppsModule.WhyOf(a.Name)) : "• " + n;
        }

        void Remove()
        {
            var sel = lv.CheckedItems.Cast<ListViewItem>().Select(i => (AppPkg)i.Tag).ToList();
            if (sel.Count == 0) return;
            string napps = sel.Count + (sel.Count == 1 ? " app" : " apps");
            // 3.38.4: Keep and Caution apps first, so the costly ones are read
            var lines = sel.OrderBy(a => a.Risk == AppRisk.Keep ? 0 : a.Risk == AppRisk.Caution ? 1 : 2).Select(ConfirmLine).ToArray();
            string body = "Undo can't bring removed apps back. Reinstall them from the Microsoft Store if you need them again.";
            if (sel.Any(a => a.Risk == AppRisk.Keep)) body = L.T("Windows or other programs need the apps marked Keep.") + " " + L.T(body);
            if (!Dlg.Confirm("Remove " + napps + "?", body, "Remove " + napps, "Cancel", true, string.Join("\n", lines))) return;
            bool d = deprov.Checked;
            MainForm.Instance.Busy("Removing apps…", () => { AppsModule.Remove(sel, d); data = AppsModule.Load(); foreach (var a in data) a.Icon = AppIcons.FromPackage(a.InstallLocation); }, () => Fill(""));
        }
    }
}
