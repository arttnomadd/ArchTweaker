// ArchTweaker: Scheduled tasks page (3.39).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    // Every scheduled task, third-party first. Tick = enabled. Each change is journaled once and can be put back.
    public class TasksPage : Panel, IPage
    {
        ListView lv; List<SchedTask> data; bool filling; CheckBox hideMs; string query = "";
        static bool msConfirmed;   // asked once per session before the first Microsoft task is switched

        public TasksPage()
        {
            BackColor = Theme.Bg;
            lv = new ListView { Dock = DockStyle.Fill, CheckBoxes = true };
            Theme.DarkList(lv);
            lv.Columns.Add("Task (checked = enabled)", 300); lv.Columns.Add(new ColumnHeader { Text = "Publisher", Width = 110, Tag = "pill" });
            lv.Columns.Add("Folder", 320); lv.Columns.Add("Author", 220);
            lv.ItemCheck += OnItemCheck;
            var bar = Theme.Bar();
            bar.Controls.Add(Theme.Btn("Run now", delegate
            {
                if (lv.SelectedItems.Count == 0) return;
                var t = (SchedTask)lv.SelectedItems[0].Tag;
                MainForm.Instance.Busy("Starting the task…", () => TasksModule.Run(t), () => Log.Say("Started the task '{0}'.", t.Name));
            }));
            bar.Controls.Add(Theme.Btn("Put back all", delegate
            {
                int n = TasksModule.Journal().Count;
                if (n == 0) { Log.Say("No scheduled task was changed from this page."); return; }
                if (!Dlg.Confirm(L.F("Put back {0} task(s)?", n), "Each task ArchTweaker changed here goes back to the state it had before.", "Put back")) return;
                int done = 0;
                MainForm.Instance.Busy("Putting tasks back…", () => { done = TasksModule.RestoreAll(); data = null; }, () => { Log.Say("Put back {0} scheduled task(s).", done); OnShow(); });
            }));
            hideMs = new TickBox { Text = "Hide Microsoft tasks", Checked = false, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(8, 6, 0, 0) };
            hideMs.CheckedChanged += delegate { Fill(); };
            bar.Controls.Add(hideMs);
            bar.Controls.Add(Theme.Btn("Refresh", delegate { data = null; OnShow(); }));
            Controls.Add(lv);
            Controls.Add(Theme.Note("Untick a task to disable it. ArchTweaker never deletes a task, and 'Put back all' or 'Undo everything' restores each one it changed. Microsoft tasks keep Windows maintained: disable one only when you know what it does."));
            Controls.Add(bar);
        }

        public void OnShow()
        {
            if (data != null) { Fill(); return; }
            Skin.Loading(lv);
            List<SchedTask> d = null;
            MainForm.Instance.Busy("Reading scheduled tasks…", () => { d = TasksModule.Load(); }, () => { data = d; Fill(); });
        }
        public void Filter(string q) { query = q ?? ""; Fill(); }

        void Fill()
        {
            if (data == null) return;
            filling = true; lv.BeginUpdate(); lv.Items.Clear();
            foreach (var t in data)
            {
                if (hideMs.Checked && t.Microsoft) continue;
                if (query.Length > 0 && (t.Full + " " + t.Author).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var it = new ListViewItem(new[] { t.Name, L.T(t.Microsoft ? "Microsoft" : "Other"), t.Path, t.Author }) { Tag = t, Checked = t.Enabled };
                it.UseItemStyleForSubItems = false;
                it.SubItems[1].Tag = t.Microsoft ? Theme.Muted : Theme.Accent;   // pill colour (TableSkin)
                it.ForeColor = t.Enabled ? Theme.Text : Theme.Muted;
                lv.Items.Add(it);
            }
            Skin.Loaded(lv, query.Length > 0 ? "No tasks match \"" + query + "\"." : "No scheduled tasks found.");
            lv.EndUpdate(); filling = false;
        }

        void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (filling) return;
            var t = (SchedTask)lv.Items[e.Index].Tag;
            bool on = e.NewValue == CheckState.Checked;
            if (!on && t.Microsoft && !msConfirmed)
            {
                if (!Dlg.Confirm("Disable a Microsoft task?", "Microsoft tasks keep Windows maintained: updates, disk checks, defragmenting and more. Disable one only when you know what it does. ArchTweaker can put it back.", "Disable", "Keep it on"))
                { e.NewValue = e.CurrentValue; return; }
                msConfirmed = true;
            }
            if (!TasksModule.SetEnabled(t, on))
            {
                e.NewValue = e.CurrentValue;
                Log.Say("Windows refused to change the task '{0}'. Windows protects some system tasks.", t.Name);
                return;
            }
            lv.Items[e.Index].ForeColor = on ? Theme.Text : Theme.Muted;
        }
    }
}
