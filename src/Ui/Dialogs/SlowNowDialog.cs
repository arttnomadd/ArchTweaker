// 3.43: the answer to "Why is my PC slow right now?": up to five plain sentences, worst first, each with the page that
// fixes it. Check() runs the 2-second check off the UI thread, then opens this dialog.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ArchTweaker
{
    public sealed class SlowNowDialog : ListDialog
    {
        readonly Button fix;

        SlowNowDialog(List<SlowNow.Finding> found)
            : base("Why is my PC slow right now?", new Size(820, 420), new Size(560, 300),
                   "What slows this PC at this moment, worst first. Select a line and click Open the fix, or double-click it.", 56, false)
        {
            Column("", 90); Column("What ArchTweaker found", 680);
            List.ShowItemToolTips = true;
            foreach (var f in found)
            {
                var it = new ListViewItem(new[] { L.T(f.Level == 2 ? "Slows it" : f.Level == 1 ? "Worth knowing" : "Fine"), f.Text }) { Tag = f, ToolTipText = f.Text };
                it.ForeColor = f.Level == 2 ? Theme.Warn : f.Level == 1 ? Theme.Text : Theme.Ok;
                List.Items.Add(it);
            }
            fix = Main("Open the fix", Open);
            Extra("Check again", () => { Close(); Check(); });
            List.SelectedIndexChanged += delegate { fix.Enabled = Selected() != null && Selected().Page != null; };
            List.ItemActivate += delegate { Open(); };
            if (List.Items.Count > 0) List.Items[0].Selected = true;
            fix.Enabled = Selected() != null && Selected().Page != null;
        }

        SlowNow.Finding Selected() { return List.SelectedItems.Count == 0 ? null : List.SelectedItems[0].Tag as SlowNow.Finding; }

        void Open()
        {
            var f = Selected(); if (f == null || f.Page == null) return;
            Close();
            AppShell.Current.ShowPage(f.Page);
        }

        // Runs the check (about 2 s, pages stay usable) and shows the answer
        public static void Check()
        {
            List<SlowNow.Finding> found = null;
            AppShell.Current.LoadData("Checking what slows this PC right now…", delegate { found = SlowNow.Check(); }, delegate
            {
                if (found == null) return;
                using (var d = new SlowNowDialog(found)) d.ShowDialog(MainForm.Instance);
            });
        }
    }
}
