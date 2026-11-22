// ArchTweaker: Services page.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ================= SERVICES =================
    public class ServicesPage : Panel, IPage
    {
        DataGridView grid; List<SvcInfo> data; CheckBox onlyKnown;
        static readonly string[] Modes = { "Automatic", "Automatic (Delayed)", "Manual", "Disabled" };
        public ServicesPage()
        {
            BackColor = Theme.Bg;
            grid = new DataGridView { Dock = DockStyle.Fill };
            Theme.DarkGrid(grid);
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Display", HeaderText = "Service", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 30, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", Width = 150, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", Width = 80, ReadOnly = true });
            var combo = new DataGridViewComboBoxColumn { Name = "Start", HeaderText = "Startup type", Width = 160, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing };
            combo.Items.AddRange(Modes); combo.Items.Add("Boot"); combo.Items.Add("System"); combo.Items.Add("?");
            grid.Columns.Add(combo);
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Group", HeaderText = "Group", Width = 95, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Note", HeaderText = "Notes", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 35, ReadOnly = true });
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.CellValueChanged += Changed;
            grid.CurrentCellDirtyStateChanged += delegate { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.DataError += delegate { };

            var bar = Theme.Bar();
            onlyKnown = new SwitchBox { Text = "Show only services with notes", Checked = true, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 6, 16, 0) };
            onlyKnown.CheckedChanged += delegate { Fill(""); };
            bar.Controls.Add(onlyKnown);
            bar.Controls.Add(Theme.Btn("Disable telemetry and safe services", delegate
            {
                if (data == null) return;
                MainForm.Instance.Busy("Disabling services…", () => { foreach (var s in data.Where(x => x.Group == "Telemetry" || x.Group == "Safe")) Svc.SetStart(s.Name, 4, false); }, OnShow);
            }));
            bar.Controls.Add(Theme.Btn("Backup to .reg", delegate
            {
                using (var d = new SaveFileDialog { Filter = "Registry file|*.reg", FileName = "services_" + DateTime.Now.ToString("yyyyMMdd") + ".reg" })
                    if (d.ShowDialog() == DialogResult.OK) ServicesModule.Backup(d.FileName);
            }));
            bar.Controls.Add(Theme.Btn("Restore from .reg", delegate
            {
                using (var d = new OpenFileDialog { Filter = "Registry file|*.reg", InitialDirectory = Paths.Backups })
                    if (d.ShowDialog() == DialogResult.OK) MainForm.Instance.Busy("Restoring services…", () => ServicesModule.Restore(d.FileName), OnShow);
            }));
            bar.Controls.Add(Theme.Btn("Refresh", delegate { OnShow(); }));
            Controls.Add(grid); Controls.Add(Theme.Note("Changes apply immediately, and Undo can reverse them. On first launch, ArchTweaker saved your original configuration to backup\\services_original.reg.")); Controls.Add(bar);
        }
        bool filling;
        public void OnShow() { Skin.Loading(grid); MainForm.Instance.Load("Loading services…", () => { data = ServicesModule.Load(); }, () => Fill("")); }
        public void Filter(string q) { Fill(q); }
        void Fill(string q)
        {
            if (data == null) return;
            filling = true; grid.Rows.Clear();
            foreach (var s in data)
            {
                if (onlyKnown.Checked && s.Group == "" && q.Length == 0) continue;
                if (q.Length > 0 && (s.Display + s.Name + s.Note).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                int i = grid.Rows.Add(s.Display, s.Name, s.Status, Svc.StartName(s.Start, s.Delayed), s.Group, s.Note);
                grid.Rows[i].Tag = s;
                if (s.Group == "Telemetry") grid.Rows[i].Cells["Group"].Style.ForeColor = Theme.Bad;
                else if (s.Group == "Safe") grid.Rows[i].Cells["Group"].Style.ForeColor = Theme.Ok;
                else if (s.Group == "Situational") grid.Rows[i].Cells["Group"].Style.ForeColor = Theme.Warn;
                if (s.Start <= 1) grid.Rows[i].Cells["Start"].ReadOnly = true;
            }
            filling = false; Skin.Loaded(grid, q.Length > 0 ? "No services match \"" + q + "\"." : "No services to show. Untick 'Known services only' to see all.");
        }
        void Changed(object sender, DataGridViewCellEventArgs e)
        {
            if (filling || e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "Start") return;
            var s = (SvcInfo)grid.Rows[e.RowIndex].Tag;
            string v = (grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value ?? "").ToString();
            int idx = Array.IndexOf(Modes, v); if (idx < 0) return;
            int start = idx <= 1 ? 2 : idx == 2 ? 3 : 4; bool delayed = idx == 1;
            Svc.SetStart(s.Name, start, delayed); s.Start = start; s.Delayed = delayed;
            Log.W("Service {0} → {1}", s.Name, v);
        }
    }
}
