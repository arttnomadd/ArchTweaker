// ArchTweaker: App settings dialog (3.30; on ListDialog since 3.31). Logic: Cleanup/AppSettings.cs.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ArchTweaker
{
    public class AppSettingsDialog : ListDialog
    {
        ListView lv { get { return List; } }
        public AppSettingsDialog() : base("App settings", new Size(860, 520), new Size(620, 380),
            "Changes settings inside other apps' own files. Close the app first: a running app writes its old settings back. ArchTweaker keeps a copy of each file before its first change, and Put back the originals restores those copies.", 70, true)
        {
            Column("Setting", 300); Column("State", 110); Column("What it does", 420);
            Main("Apply ticked settings", ApplyTicked);
            Extra("Put back the originals", Restore);
            Fill(true);
        }
        void Fill(bool defaults)
        {
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var s in AppSettings.All)
            {
                bool inst = AppSettings.Installed(s), on = inst && AppSettings.IsOn(s);
                string state = !inst ? L.T("not installed") : on ? L.T("on") : AppSettings.Running(s) ? L.T("app is running") : L.T("off");
                var it = new ListViewItem(new[] { L.T(s.Title), state, L.T(s.Desc) }) { Tag = s, Checked = defaults && inst && !on && s.Default };
                if (!inst) it.ForeColor = Theme.Muted;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
        }
        void ApplyTicked()
        {
            var picks = lv.CheckedItems.Cast<ListViewItem>().Select(i => (AppSetting)i.Tag).Where(AppSettings.Installed).ToList();
            if (picks.Count == 0) { Dlg.Info("Nothing ticked", "Tick the settings to change, then Apply ticked settings."); return; }
            List<string> res;
            UseWaitCursor = true;
            try { res = AppSettings.ApplyAll(picks); } finally { UseWaitCursor = false; }
            Fill(false);
            Dlg.Info("App settings", "The changes count the next time each app starts.", string.Join("\r\n", res.ToArray()));
        }
        void Restore()
        {
            if (!AppSettings.HasBackups) { Dlg.Info("Nothing to put back", "ArchTweaker has not changed any app files."); return; }
            if (!Dlg.Confirm("Put back the original app files?", "Each file goes back to how it was before ArchTweaker first changed it. You lose settings that you changed in the apps since then.", "Put back")) return;
            Log.Say(AppSettings.RestoreAll());
            Fill(false);
        }
    }
}
