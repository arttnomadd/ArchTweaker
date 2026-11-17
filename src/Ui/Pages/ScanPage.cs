// ArchTweaker: System scan page.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
}

namespace ArchTweaker
{

    // Whole-PC audit: reads registry, services, tasks, power plan, boot config and network state for every tweak.
    public class ScanPage : Panel, IPage
    {
        DataGridView grid; ComboBox filter; Label summary; TextBox raw; List<ScanRow> rows; SplitContainer split;
        public ScanPage()
        {
            BackColor = Theme.Bg;
            grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true };
            Theme.DarkGrid(grid);
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "State", Width = 70 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Cat", HeaderText = "Category", Width = 150 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "Tweak", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 55 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Risk", HeaderText = "Risk", Width = 85 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Impact", HeaderText = "Impact", Width = 80 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Gain", HeaderText = "Expected gain", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 70 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Src", HeaderText = "Read from", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 45 });
            grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var r = (ScanRow)grid.Rows[e.RowIndex].Tag;
                MainForm.Instance.ShowPage(r.T.Category);
                MainForm.Instance.ShowDetails(r.T);
            };

            raw = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = Theme.LogBg, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Mono,
                Text = "Raw system state appears here: boot configuration (bcdedit), TCP/network settings, memory manager, active power plan values, timer resolution, key registry values and services. Click 'Raw system state'." };
            split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Theme.Bg, SplitterWidth = 6 };
            split.Panel1.Controls.Add(grid); split.Panel2.Controls.Add(raw);
            split.Panel1.BackColor = Theme.Bg; split.Panel2.BackColor = Theme.Bg;
            HandleCreated += delegate { try { split.SplitterDistance = Math.Max(200, (int)(Height * 0.62)); } catch { } };

            var bar = Theme.Bar();
            bar.Controls.Add(Theme.Lead("Scan whole PC", delegate { Scan(); }));
            filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, BackColor = Theme.Panel, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 5, 8, 0) };
            filter.Items.AddRange(new object[] { "Show all", "Only ON (active)", "Only OFF (not active)", "Only not available", "OFF with medium/high gain" });
            filter.SelectedIndex = 0; filter.SelectedIndexChanged += delegate { Fill(""); };
            bar.Controls.Add(filter);
            bar.Controls.Add(Theme.Btn("Show raw system state", delegate { string t = null; MainForm.Instance.Busy("Reading system state…", () => { t = Scanner.RawState(); }, () => { raw.Text = t; }); }));
            bar.Controls.Add(Theme.Btn("Export report…", delegate { Export(); }));
            summary = new Label { AutoSize = true, ForeColor = Theme.Text, Font = Theme.FB, Margin = new Padding(12, 8, 0, 0) };
            bar.Controls.Add(summary);
            Controls.Add(split);
            Controls.Add(Theme.Note("Reads the live state of every tweak straight from the registry, services, scheduled tasks, power plan (powercfg), boot config (bcdedit) and network stack (PowerShell). It shows changes made by Windows, by you or by other tools too. Double-click a row to open it."));
            Controls.Add(bar);
        }
        bool done;
        public void OnShow() { if (!done) Scan(); }
        public void Filter(string q) { Fill(q); }

        void Scan()
        {
            done = true;
            List<ScanRow> r = null;
            Skin.Loading(grid);
            MainForm.Instance.Busy("Scanning the whole PC…", () => { r = Scanner.Run(null); }, () => { rows = r; Fill(""); });
        }

        void Fill(string q)
        {
            if (rows == null) return;
            grid.Rows.Clear();
            int f = filter.SelectedIndex;
            foreach (var r in rows)
            {
                if (f == 1 && r.Status != "ON") continue;
                if (f == 2 && r.Status != "OFF") continue;
                if (f == 3 && r.Status != "N/A") continue;
                if (f == 4 && !(r.Status == "OFF" && r.T.Impact >= 2)) continue;
                if (q.Length > 0 && (r.T.Title + r.T.Category + r.Source).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string risk = r.T.Risk == Risk.Safe ? "safe" : r.T.Risk == Risk.Moderate ? "moderate" : "advanced";
                int i = grid.Rows.Add(r.Status, r.T.Category, r.T.Title, risk, r.T.ImpactName.ToLowerInvariant(), r.T.Gain, r.Source);
                grid.Rows[i].Cells["Impact"].Style.ForeColor = r.T.Impact >= 3 ? Theme.Ok : r.T.Impact == 2 ? Theme.Accent : r.T.Impact == 1 ? Theme.Text : Theme.Muted;
                grid.Rows[i].Tag = r;
                var c = grid.Rows[i].Cells["Status"];
                c.Style.ForeColor = r.Status == "ON" ? Theme.Ok : r.Status == "OFF" ? Theme.Muted : r.Status == "ERROR" ? Theme.Bad : Theme.Warn;
                c.Style.Font = Theme.FB;
                if (r.T.Risk != Risk.Safe) grid.Rows[i].Cells["Risk"].Style.ForeColor = r.T.Risk == Risk.Advanced ? Theme.Risk : Theme.Warn;
            }
            int on = rows.Count(x => x.Status == "ON"), off = rows.Count(x => x.Status == "OFF"), na = rows.Count(x => x.Status == "N/A");
            summary.Text = string.Format("{0} ON  ·  {1} OFF  ·  {2} not available  ·  {3} checked", on, off, na, rows.Count);
            Skin.Loaded(grid, q.Length > 0 ? "No results match \"" + q + "\"." : "Nothing matches this filter.");
        }

        void Export()
        {
            if (rows == null) return;
            using (var d = new SaveFileDialog { Filter = "Text report|*.txt|CSV (Excel)|*.csv", FileName = "ArchTweaker-scan-" + DateTime.Now.ToString("yyyyMMdd-HHmm") })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                var sb = new System.Text.StringBuilder();
                if (d.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("State;Category;Tweak;Risk;Impact;Expected gain;Read from;Id");
                    foreach (var r in rows) sb.AppendLine(string.Join(";", new[] { r.Status, r.T.Category, r.T.Title, r.T.Risk.ToString(), r.T.ImpactName, r.T.Gain, r.Source, r.T.Id }.Select(x => "\"" + (x ?? "").Replace("\"", "'") + "\"")));
                }
                else
                {
                    sb.AppendLine("ArchTweaker " + Program.Version + " — PC scan " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " — " + OSInfo.Name);
                    sb.AppendLine(summary.Text).AppendLine();
                    foreach (var g in rows.GroupBy(r => r.T.Category))
                    {
                        sb.AppendLine("## " + g.Key);
                        foreach (var r in g) sb.AppendLine("  [" + r.Status.PadRight(3) + "] " + r.T.Title + "  — impact " + r.T.ImpactName + ": " + r.T.Gain + "   (read from: " + r.Source + ")");
                        sb.AppendLine();
                    }
                    if (raw.Text.StartsWith("ArchTweaker")) sb.AppendLine(raw.Text);
                }
                System.IO.File.WriteAllText(d.FileName, sb.ToString(), new System.Text.UTF8Encoding(true));
                Log.W("Scan report saved: {0}", d.FileName);
            }
        }
    }
}
