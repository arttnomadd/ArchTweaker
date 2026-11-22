// ArchTweaker: Power settings explorer page: every setting of every power plan.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ===================== POWER SETTINGS =====================
    public class PowerSettingsPage : Panel, IPage
    {
        ComboBox schemes; TextBox search; CheckBox hiddenOnly; ListView lv;
        Label desc, acLbl, dcLbl;
        ComboBox acCombo, dcCombo; NumericUpDown acNum, dcNum;
        Button apply, hideBtn, regBtn, copyBtn, revertBtn;
        List<PowerSetting> all = new List<PowerSetting>();
        PowerSetting cur;
        bool loaded;
        int loadToken;   // drops results of a load that a newer scheme selection superseded

        public PowerSettingsPage()
        {
            BackColor = Theme.Bg;
            var bar = Theme.Bar();
            schemes = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, BackColor = Theme.Panel, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 5, 8, 0) };
            schemes.SelectedIndexChanged += delegate { if (loaded) LoadSettings(); };
            search = new TextBox { Width = 200, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 6, 8, 0) };
            search.TextChanged += delegate { Render(); };
            hiddenOnly = new SwitchBox { Text = "Hidden only", ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, 8, 8, 0) };
            hiddenOnly.CheckedChanged += delegate { Render(); };
            bar.Controls.Add(schemes); bar.Controls.Add(search); bar.Controls.Add(hiddenOnly);
            bar.Controls.Add(Theme.Btn("Refresh", delegate { OnShowForce(); }));
            revertBtn = Theme.Btn("Revert my edits", delegate { RevertEdits(); });
            bar.Controls.Add(revertBtn);
            bar.Controls.Add(Theme.Btn("Open in Power Options", delegate
            {
                var s = schemes.SelectedItem as PowerScheme;
                Shell.OpenVisible("rundll32.exe", "shell32.dll,Control_RunDLL PowerCfg.cpl @0,/editplan:" + (s != null ? s.Id.ToString() : ""));
            }));

            lv = new ListView { Dock = DockStyle.Fill, MultiSelect = false };
            Theme.DarkList(lv);
            lv.Columns.Add("Subgroup", 170); lv.Columns.Add("Setting", 300); lv.Columns.Add("Hidden", 60);
            lv.Columns.Add("Plugged in (AC)", 140); lv.Columns.Add("Battery (DC)", 140); lv.Columns.Add("Values", 260);
            lv.SelectedIndexChanged += delegate { Select(); };

            var edit = new Panel { Dock = DockStyle.Bottom, Height = 150, BackColor = Theme.Panel, Padding = new Padding(12, 8, 12, 8) };
            desc = new Label { Dock = DockStyle.Top, Height = 54, ForeColor = Theme.Muted, Font = Theme.FS, UseMnemonic = false, AutoEllipsis = true, Text = "Select a setting. Hidden settings are the ones Windows doesn't show in Power Options." };
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, BackColor = Theme.Panel };
            acLbl = new Label { Text = "AC:", ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, 8, 4, 0) };
            dcLbl = new Label { Text = "DC:", ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(12, 8, 4, 0) };
            acCombo = MakeCombo(); dcCombo = MakeCombo(); acNum = MakeNum(); dcNum = MakeNum();
            apply = Theme.Lead("Apply value", delegate { ApplyValue(); });
            hideBtn = Theme.Btn("Unhide", delegate { ToggleHidden(); });
            regBtn = Theme.Btn("Open in Regedit", delegate { if (cur != null) PowerExplorer.OpenInRegedit(cur); });
            copyBtn = Theme.Btn("Copy powercfg command", delegate { CopyCmd(); });
            foreach (Control c in new Control[] { acLbl, acCombo, acNum, dcLbl, dcCombo, dcNum, apply, hideBtn, regBtn, copyBtn }) row.Controls.Add(c);
            edit.Controls.Add(row); edit.Controls.Add(desc);
            SetEditor(null);

            Controls.Add(lv);
            Controls.Add(edit);
            Controls.Add(Theme.Note("Every power setting of the selected plan, including hidden ones. ArchTweaker writes edits to that plan with the same API that Power Options uses. It keeps the original value, so \"Revert my edits\" or Undo everything puts it back exactly."));
            Controls.Add(bar);
        }

        ComboBox MakeCombo() { return new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, BackColor = Theme.Bg, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 5, 0, 0) }; }
        NumericUpDown MakeNum() { return new NumericUpDown { Width = 110, BackColor = Theme.Bg, ForeColor = Theme.Text, Margin = new Padding(0, 5, 0, 0) }; }

        public void Filter(string q) { }
        public void OnShow() { if (!loaded) OnShowForce(); }

        void OnShowForce()
        {
            List<PowerScheme> s = null;
            MainForm.Instance.Load("Reading power plans…", delegate { s = PowerExplorer.Schemes(); }, delegate
            {
                loaded = false;
                schemes.Items.Clear();
                if (s != null) foreach (var x in s) schemes.Items.Add(x);
                int act = s == null ? -1 : s.FindIndex(x => x.Active);
                if (schemes.Items.Count > 0) schemes.SelectedIndex = act >= 0 ? act : 0;
                loaded = true;
                LoadSettings();
            });
        }

        void LoadSettings()
        {
            var s = schemes.SelectedItem as PowerScheme; if (s == null) return;
            List<PowerSetting> l = null; int token = ++loadToken;
            MainForm.Instance.Load("Reading power settings…", delegate { l = PowerExplorer.Load(s.Id); }, delegate
            {
                if (token != loadToken) return;          // a newer load is in flight
                all = l ?? new List<PowerSetting>();
                cur = null; SetEditor(null);
                Render();
                UpdateRevert();
            });
        }

        void UpdateRevert() { int n = PowerExplorer.EditedCount(); revertBtn.Text = "Revert my edits" + (n > 0 ? " (" + n + ")" : ""); }

        void Render()
        {
            string q = search.Text.Trim(); bool none = true;
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var p in all.OrderBy(x => x.SubName).ThenBy(x => x.Name))
            {
                if (hiddenOnly.Checked && !p.Hidden) continue;
                if (q.Length > 0 && (p.Name + " " + p.SubName + " " + p.Id + " " + p.Desc).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var it = new ListViewItem(new[] { p.SubName, p.Name, p.Hidden ? "yes" : "", p.HasValue ? p.Show(p.Ac) : "", p.HasValue ? p.Show(p.Dc) : "", p.Range }) { Tag = p };
                if (p.Hidden) it.ForeColor = Theme.Muted;
                lv.Items.Add(it);
                none = false;
            }
            lv.EndUpdate(); Skin.SetEmpty(lv, none ? (q.Length > 0 ? "No power settings match \"" + q + "\"." : all.Count == 0 ? "Loading…" : "No settings to show.") : null);
        }

        new void Select()
        {
            cur = lv.SelectedItems.Count > 0 ? (PowerSetting)lv.SelectedItems[0].Tag : null;
            SetEditor(cur);
        }

        void SetEditor(PowerSetting p)
        {
            bool has = p != null;
            foreach (Control c in new Control[] { acLbl, dcLbl, hideBtn, regBtn }) c.Enabled = has;
            foreach (Control c in new Control[] { apply, copyBtn, acCombo, dcCombo, acNum, dcNum }) c.Enabled = has && p.HasValue;
            acCombo.Visible = dcCombo.Visible = has && p.IsEnum;
            acNum.Visible = dcNum.Visible = has && !p.IsEnum;
            if (!has) return;
            desc.Text = p.Name + "  —  {" + p.Id + "}\r\n" + (string.IsNullOrEmpty(p.Desc) ? "(no description)" : p.Desc);
            hideBtn.Text = p.Hidden ? "Unhide in Power Options" : "Hide in Power Options";
            if (p.IsEnum)
            {
                foreach (var cb in new[] { acCombo, dcCombo })
                {
                    cb.Items.Clear();
                    foreach (var o in p.Options) cb.Items.Add(new Opt { V = o.Key, T = o.Value });
                }
                SelectOpt(acCombo, p.Ac); SelectOpt(dcCombo, p.Dc);
            }
            else
            {
                foreach (var n in new[] { acNum, dcNum })
                {
                    n.Minimum = p.Min; n.Maximum = Math.Max(p.Min, p.Max); n.Increment = p.Inc > 0 ? p.Inc : 1;
                }
                acNum.Value = Clamp(acNum, p.Ac); dcNum.Value = Clamp(dcNum, p.Dc);
            }
        }

        class Opt { public uint V; public string T; public override string ToString() { return T + "  (" + V + ")"; } }
        static void SelectOpt(ComboBox cb, uint v) { for (int i = 0; i < cb.Items.Count; i++) if (((Opt)cb.Items[i]).V == v) { cb.SelectedIndex = i; return; } if (cb.Items.Count > 0) cb.SelectedIndex = 0; }
        static decimal Clamp(NumericUpDown n, uint v) { decimal d = v; return d < n.Minimum ? n.Minimum : d > n.Maximum ? n.Maximum : d; }

        bool Values(out uint ac, out uint dc)
        {
            ac = dc = 0;
            if (cur == null) return false;
            if (cur.IsEnum)
            {
                var a = acCombo.SelectedItem as Opt; var d = dcCombo.SelectedItem as Opt;
                if (a == null || d == null) return false;
                ac = a.V; dc = d.V;
            }
            else { ac = (uint)acNum.Value; dc = (uint)dcNum.Value; }
            return true;
        }

        void ApplyValue()
        {
            uint ac, dc; if (!Values(out ac, out dc)) return;
            var p = cur; if (p == null || !p.HasValue) return;
            uint? wa = ac != p.Ac ? (uint?)ac : null, wd = dc != p.Dc ? (uint?)dc : null;   // only the side(s) you changed
            if (!wa.HasValue && !wd.HasValue) { Log.W("No change for '{0}'.", p.Name); return; }
            string err = null;
            MainForm.Instance.Busy("Writing power setting…", delegate { try { PowerExplorer.Write(p, wa, wd); } catch (Exception ex) { err = ex.Message; } }, delegate
            {
                if (err != null) Dlg.Error("Couldn't set the power value", "Windows rejected the change, so the value is unchanged.", err);
                Render(); UpdateRevert();
            });
        }

        void ToggleHidden()
        {
            var p = cur; if (p == null) return;
            bool hide = !p.Hidden;
            MainForm.Instance.Busy(hide ? "Hiding…" : "Unhiding…", delegate { PowerExplorer.SetHidden(p, hide); }, delegate { Render(); SetEditor(p); });
        }

        void CopyCmd()
        {
            uint ac, dc;
            if (cur == null || !Values(out ac, out dc)) return;
            try { Clipboard.SetText(PowerExplorer.PowercfgCommand(cur, ac, dc)); Log.W("powercfg commands copied to clipboard."); }
            catch (Exception ex) { Log.W("Clipboard copy failed: {0}", ex.Message); }
        }

        void RevertEdits()
        {
            if (PowerExplorer.EditedCount() == 0) { Dlg.Info("Nothing to revert", "You have not edited power values on this page."); return; }
            if (!Dlg.Confirm("Revert your power edits?", "Every setting you changed on this page goes back to the value it had before.", "Revert edits", "Keep edits")) return;
            MainForm.Instance.Busy("Restoring power values…", delegate { PowerExplorer.RevertAll(); }, delegate { LoadSettings(); });
        }
    }
}
