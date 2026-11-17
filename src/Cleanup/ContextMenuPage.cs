// ArchTweaker: Context menu manager page.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ===================== CONTEXT MENU =====================
    public class ContextMenuPage : Panel, IPage
    {
        ListView lv; CheckBox showBuiltin; List<CtxItem> items = new List<CtxItem>(); bool filling;
        readonly Dictionary<CtxItem, bool> pending = new Dictionary<CtxItem, bool>();   // unapplied ticks survive filter/refresh

        public ContextMenuPage()
        {
            BackColor = Theme.Bg;
            lv = new ListView { Dock = DockStyle.Fill, CheckBoxes = true };
            Theme.DarkList(lv);
            lv.Columns.Add("Entry", 280); lv.Columns.Add("Type", 120); lv.Columns.Add("Shows on", 160); lv.Columns.Add("File / command", 420);
            lv.ItemChecked += (s, e) =>
            {
                if (filling) return;
                var it = (CtxItem)e.Item.Tag;
                if (e.Item.Checked == it.Enabled) pending.Remove(it); else pending[it] = e.Item.Checked;
            };
            var bar = Theme.Bar();
            bar.Controls.Add(Theme.Lead("Apply changes", delegate { Apply(); }));
            bar.Controls.Add(Theme.Btn("Restart Explorer", delegate { Tools.RestartExplorer(); }));
            bar.Controls.Add(Theme.Btn("Refresh", delegate { OnShow(); }));
            showBuiltin = new SwitchBox { Text = "Show Windows built-in entries", ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(8, 8, 0, 0) };
            showBuiltin.CheckedChanged += delegate { Render(); };
            bar.Controls.Add(showBuiltin);
            Controls.Add(lv);
            Controls.Add(Theme.Note("Untick an entry to remove it from the right-click menu. Then click Apply and restart Explorer. ArchTweaker uses Windows' own switches (Shell Extensions\\Blocked and LegacyDisable), so it deletes nothing and Undo everything restores it. On Windows 11 these entries are under \"Show more options\"."));
            Controls.Add(bar);
        }

        public void Filter(string q) { }

        public void OnShow()
        {
            if (pending.Count > 0 && !Dlg.Confirm("Discard " + pending.Count + (pending.Count == 1 ? " change" : " changes") + " and reload?", "You have not applied your ticks and unticks yet. Reload reads the menu again and drops them.", "Discard and reload", "Keep editing")) return;
            pending.Clear();
            List<CtxItem> l = null;
            Skin.Loading(lv);
            MainForm.Instance.Busy("Reading context menu entries…", delegate { l = CtxMenu.Load(); }, delegate { items = l ?? new List<CtxItem>(); Render(); Skin.Loaded(lv, "No entries to show. Tick 'Show Windows built-ins' to see the built-in ones."); });
        }

        void Render()
        {
            filling = true;
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var it in items)
            {
                if (it.Builtin && !showBuiltin.Checked) continue;
                bool want; if (!pending.TryGetValue(it, out want)) want = it.Enabled;
                var li = new ListViewItem(new[] { it.Name, it.Kind, it.AppliesTo, it.Target ?? "" }) { Tag = it, Checked = want };
                if (it.Builtin) li.ForeColor = Theme.Muted;
                lv.Items.Add(li);
            }
            lv.EndUpdate();
            filling = false;
        }

        void Apply()
        {
            if (filling) return;
            var changes = pending.Where(kv => kv.Key.Enabled != kv.Value).ToList();
            if (changes.Count == 0) { Dlg.Info("No changes to apply", "Tick or untick entries first, then select Apply."); return; }
            if (changes.Any(kv => kv.Key.Builtin && !kv.Value) &&
                !Dlg.Confirm("Turn off a Windows built-in entry?", "Some Windows features use it. You can turn it back on here or with Undo everything.", "Turn off", "Cancel", true)) return;
            int fail = 0;
            MainForm.Instance.Busy("Applying context menu changes…", delegate
            {
                foreach (var kv in changes)
                {
                    try
                    {
                        CtxMenu.Set(kv.Key, kv.Value);
                        // a shell extension is blocked per CLSID, so every row of the same CLSID changes with it
                        if (kv.Key.IsExtension) foreach (var o in items.Where(x => x.IsExtension && string.Equals(x.Clsid, kv.Key.Clsid, StringComparison.OrdinalIgnoreCase))) o.Enabled = kv.Value;
                    }
                    catch (Exception ex) { fail++; Log.W("Context menu change failed for {0}: {1}", kv.Key.Name, ex.Message); }
                }
            }, delegate
            {
                pending.Clear();
                Render();
                Log.W("{0} context-menu change(s) applied{1}. Restart Explorer to see them.", changes.Count - fail, fail > 0 ? ", " + fail + " failed" : "");
            });
        }
    }
}
