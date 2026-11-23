// ArchTweaker: Foreground boost picker dialog (3.30; on ListDialog since 3.31). Logic: Power/ForegroundBoost.cs.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ArchTweaker
{
    public class ForegroundBoostDialog : ListDialog
    {
        ListView lv { get { return List; } }
        readonly Label now;
        public ForegroundBoostDialog() : base("Foreground boost (Win32PrioritySeparation)", new Size(760, 560), new Size(560, 420),
            "How Windows shares CPU time between the window you use and everything else. Short, variable quanta with a 3:1 boost (0x26) is ArchTweaker's pick. No value is best on every PC: set one, then measure it on the A/B test page. Undo everything puts back the value you had before.", 84, false)
        {
            lv.MultiSelect = false; lv.HideSelection = false;
            Column("Value", 90); Column("What it does", 600);
            now = new Label { Dock = DockStyle.Top, Height = 28, Padding = new Padding(16, 4, 16, 4), ForeColor = Theme.Muted, UseMnemonic = false };
            AddStrip(now);
            Main("Set this value", SetSelected);
            Extra("Put back the Windows default", () => Apply(ForegroundBoost.WindowsDefault));
            lv.DoubleClick += delegate { SetSelected(); };
            Fill();
        }
        void Fill()
        {
            int? cur = ForegroundBoost.Current();
            now.Text = L.T("Now:") + " " + (cur.HasValue ? "0x" + cur.Value.ToString("X2") + " — " + ForegroundBoost.Describe(cur.Value) : L.T("not set (Windows uses its default)"));
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (int v in ForegroundBoost.Values)
            {
                string d = ForegroundBoost.Describe(v);
                if (v == 0x26) d += "  · " + L.T("ArchTweaker's pick");
                if (!ForegroundBoost.BoostWorks(v) && (v & 3) != 0) d += "  · " + L.T("the boost has no effect with fixed quanta");
                var it = new ListViewItem(new[] { "0x" + v.ToString("X2") + " (" + v + ")", d }) { Tag = v };
                if (cur.HasValue && cur.Value == v) { it.Font = Theme.FB; it.Selected = true; }
                lv.Items.Add(it);
            }
            lv.EndUpdate();
        }
        void SetSelected()
        {
            if (lv.SelectedItems.Count == 0) { Dlg.Info("Choose a value first", "Click a row, then Set this value."); return; }
            Apply((int)lv.SelectedItems[0].Tag);
        }
        void Apply(int v)
        {
            try { ForegroundBoost.Set(v); }
            catch (Exception ex) { Dlg.Error("ArchTweaker could not set the value", ex.Message); return; }
            Fill();
            if (MainForm.Instance != null) { AppShell.Current.Notify(L.F("Win32PrioritySeparation is 0x{0}. It takes effect at once.", v.ToString("X2")), Toast.Kind.Ok); AppShell.Current.RefreshAll(); }
        }
    }
}
