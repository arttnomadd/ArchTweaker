// ArchTweaker: Ghost devices dialog (2.x; moved here and onto ListDialog in 3.31).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ArchTweaker
{
    public class GhostDialog : ListDialog
    {
        ListView lv { get { return List; } }
        List<GhostDevice> devs = new List<GhostDevice>();
        public GhostDialog() : base("Ghost devices", new Size(820, 560), new Size(600, 400),
            "Devices that Windows still keeps drivers and settings for, but that are not connected: old USB sticks, monitors, previous network adapters and GPUs. Removing them is safe. Windows sets up a device again when you plug it in.", 64, true)
        {
            Column("Device", 330); Column("Class", 140); Column("Instance ID", 300);
            Main("Remove ticked devices", RemoveTicked);
            Extra("Tick all", () => { foreach (ListViewItem it in lv.Items) it.Checked = true; });
            Shown += delegate { Load2(); };
        }
        void Load2()
        {
            Skin.Loading(lv);
            List<GhostDevice> found = null; string err = null;
            var t = new System.Threading.Thread(() => { try { found = Ghosts.List(); } catch (Exception ex) { err = ex.Message; } }) { IsBackground = true };
            t.Start();
            var tm = new Timer { Interval = 150 };
            tm.Tick += delegate
            {
                if (IsDisposed) { tm.Stop(); tm.Dispose(); return; }   // 3.27.1: closed while loading: stop ticking
                if (t.IsAlive) return;
                tm.Stop(); tm.Dispose();
                devs = found ?? new List<GhostDevice>();
                lv.BeginUpdate(); lv.Items.Clear();
                foreach (var d in devs) lv.Items.Add(new ListViewItem(new[] { d.Name, d.Class, d.Id }) { Tag = d });
                lv.EndUpdate();
                Skin.Loaded(lv, err != null ? L.T("Could not read the device list:") + " " + err : L.T("No ghost devices: every device Windows knows is connected."));
                Text = L.T("Ghost devices") + (devs.Count > 0 ? " (" + devs.Count + ")" : "");
            };
            tm.Start();
        }
        void RemoveTicked()
        {
            var pick = lv.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (GhostDevice)i.Tag).ToList();
            if (pick.Count == 0) { MainForm.Instance.Notify("Tick the devices to remove first.", Toast.Kind.Warn); return; }
            if (!Dlg.Confirm("Remove " + pick.Count + (pick.Count == 1 ? " ghost device?" : " ghost devices?"), "Their drivers stay installed, so Windows sets up each device again if you connect it later. Undo can't bring the entries back, but you lose nothing.", "Remove", "Cancel", true,
                string.Join("\n", pick.Take(15).Select(p => "• " + p.Name).ToArray()) + (pick.Count > 15 ? "\n…" : ""))) return;
            // 3.27.1: off the UI thread, and the message says what did not work
            int n = 0, failed = 0; string err = null;
            Enabled = false; Cursor = Cursors.WaitCursor;
            var th = new System.Threading.Thread(() => { try { n = Ghosts.Remove(pick, out failed); } catch (Exception ex) { err = ex.Message; failed = pick.Count; Log.W("Ghost devices: {0}", ex.Message); } }) { IsBackground = true };
            th.Start();
            var tm = new Timer { Interval = 150 };
            tm.Tick += delegate
            {
                if (IsDisposed) { tm.Stop(); tm.Dispose(); return; }
                if (th.IsAlive) return;
                tm.Stop(); tm.Dispose(); Enabled = true; Cursor = Cursors.Default;
                if (err != null) MainForm.Instance.Notify(L.T("Could not remove the ghost devices:") + " " + err, Toast.Kind.Warn);
                else if (failed > 0) MainForm.Instance.Notify(L.F("{0} removed, {1} not removed. The log says why.", n, failed), Toast.Kind.Warn);
                else MainForm.Instance.Notify(n == 1 ? "1 ghost device removed." : n + " ghost devices removed.", Toast.Kind.Ok);
                Load2();
            };
            tm.Start();
        }
    }
}
