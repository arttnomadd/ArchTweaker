// ArchTweaker: Leftover tweaks from other packs dialog (3.6; moved here and onto ListDialog in 3.31).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace ArchTweaker
{
    public class PackLeftoverDialog : ListDialog
    {
        ListView lv { get { return List; } }
        List<PackLeftover> found = new List<PackLeftover>();
        public PackLeftoverDialog() : base("Leftover tweaks from other packs", new Size(900, 580), new Size(640, 400),
            "Values that other tweak packs leave behind. Windows does not use them, or they cause problems. The list does not show values that a tweak you applied in ArchTweaker owns. Harmful ones are ticked. ArchTweaker records every fix, so 'Undo everything' brings the old values back.", 78, true)
        {
            Column("Finding", 290); Column("Effect", 110); Column("Values", 440);
            var why = new Label { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(16, 6, 16, 6), ForeColor = Theme.Muted, UseMnemonic = false, Tag = "notr" };
            lv.SelectedIndexChanged += delegate { why.Text = lv.SelectedItems.Count == 0 ? "" : L.T(((PackLeftover)lv.SelectedItems[0].Tag).Why); };
            AddStrip(why);
            Main("Fix ticked", FixTicked);
            Shown += delegate { Reload(); };
        }
        void Reload()
        {
            try { found = Leftovers.Scan(); } catch (Exception ex) { Log.W("Leftover scan: {0}", ex.Message); found = new List<PackLeftover>(); }
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var c in found)
                lv.Items.Add(new ListViewItem(new[] { L.T(c.Title), L.T(c.Harmful ? "Harmful" : "No effect"), c.Shown }) { Tag = c, Checked = c.Harmful, ForeColor = c.Harmful ? Theme.Bad : Theme.Text });
            lv.EndUpdate();
            Skin.Loaded(lv, L.T("Nothing found: no leftover values from other tweak packs."));
            Text = L.T("Leftover tweaks from other packs") + (found.Count > 0 ? " (" + found.Count + ")" : "");
        }
        void FixTicked()
        {
            var pick = lv.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (PackLeftover)i.Tag).ToList();
            if (pick.Count == 0) return;
            int n = Leftovers.Fix(pick);
            MainForm.Instance.Notify(n == 1 ? "1 leftover value fixed. Restart to finish." : n + " leftover values fixed. Restart to finish.", Toast.Kind.Ok);
            Reload();
        }
    }
}
