// ArchTweaker: Startup page (autorun entries).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ================= AUTORUN =================
    public class AutorunPage : Panel, IPage
    {
        ListView lv; List<AutoItem> data; bool filling;
        public AutorunPage()
        {
            BackColor = Theme.Bg;
            lv = new ListView { Dock = DockStyle.Fill, CheckBoxes = true };
            Theme.DarkList(lv);
            lv.Columns.Add("Name (checked = runs at startup)", 260); lv.Columns.Add("Location", 170); lv.Columns.Add("Command", 520);
            lv.ItemChecked += (s, e) =>
            {
                if (filling) return;
                var it = (AutoItem)e.Item.Tag;
                AutorunModule.SetEnabled(it, e.Item.Checked);
                e.Item.ForeColor = it.Enabled ? Theme.Text : Theme.Muted;
                Log.W("Startup: {0} {1}", it.Name, it.Enabled ? "enabled" : "disabled");
            };
            var bar = Theme.Bar();
            bar.Controls.Add(Theme.Btn("Delete selected", delegate
            {
                if (lv.SelectedItems.Count == 0) return;
                var it = (AutoItem)lv.SelectedItems[0].Tag;
                if (Dlg.Confirm("Delete startup entry \"" + it.Name + "\"?", "Deleting removes the entry for good. Disabling also stops it from starting, and you can turn it back on later.", "Delete entry", "Keep entry", true)) { AutorunModule.Delete(it); OnShow(); }
            }));
            bar.Controls.Add(Theme.Btn("Open file location", delegate
            {
                if (lv.SelectedItems.Count == 0) return;
                var it = (AutoItem)lv.SelectedItems[0].Tag;
                string exe = ExtractExe(it.Command);
                if (exe != null) Shell.OpenVisible("explorer.exe", "/select,\"" + exe + "\"");
            }));
            bar.Controls.Add(Theme.Btn("Refresh", delegate { OnShow(); }));
            Controls.Add(lv); Controls.Add(Theme.Note("Uncheck to disable, the same way Task Manager does it. Fully reversible. The entry stays in the list.")); Controls.Add(bar);
        }
        static string ExtractExe(string cmd)
        {
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (cmd.StartsWith("\"")) { int e = cmd.IndexOf('"', 1); return e > 0 ? cmd.Substring(1, e - 1) : null; }
            int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return i > 0 ? cmd.Substring(0, i + 4) : (File.Exists(cmd) ? cmd : null);
        }
        public void OnShow() { data = AutorunModule.Load(); Fill(""); }
        public void Filter(string q) { Fill(q); }
        void Fill(string q)
        {
            filling = true; lv.BeginUpdate(); lv.Items.Clear();
            foreach (var a in data)
            {
                if (q.Length > 0 && (a.Name + a.Command).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var it = new ListViewItem(new[] { a.Name, a.Location, a.Command }) { Tag = a, Checked = a.Enabled, ForeColor = a.Enabled ? Theme.Text : Theme.Muted };
                string exe = null; try { exe = ExtractExe(a.Command); } catch { }
                AppIcons.Attach(lv, it, "exe|" + (exe ?? ""), exe == null ? null : AppIcons.FromIconSpec(exe));   // 3.3: the program's own icon
                lv.Items.Add(it);
            }
            lv.EndUpdate(); filling = false;
            Skin.Loaded(lv, q.Length > 0 ? "No startup entries match \"" + q + "\"." : "Nothing starts with Windows from the Run keys or Startup folders.");
        }
    }
}
