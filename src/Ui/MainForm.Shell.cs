// ArchTweaker main window: Log panel, tray, closing, setup guide and toasts.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    public partial class MainForm
    {
        void ToggleLog()
        {
            logOpen = !logOpen; sessionLog = logOpen;
            bottom.Height = Theme.S(logOpen ? 204 : 56); logToggle.Text = L.T(logOpen ? "Hide log" : "Show log"); logBar.Visible = logOpen;
            tailLine.Visible = !logOpen;
            if (logOpen) { logBox.SelectionStart = logBox.TextLength; logBox.ScrollToCaret(); }
        }

        public void ShowFromTray() { if (rebuildWhenShown) { rebuildWhenShown = false; Show(); Rebuild(); return; } Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); }

        public void ExitFromTray() { exiting = true; Close(); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && !rebuilding && e.CloseReason == CloseReason.UserClosing && tray != null && Tray.CloseToTray) { e.Cancel = true; Hide(); return; }
            if (e.CloseReason == CloseReason.WindowsShutDown && busyCount > 0) { e.Cancel = true; return; }   // 3.30
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e) { if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; } base.OnFormClosed(e); }

        void ReloadLog() { logBox.Text = LogStore.Text(LogStore.Filter); logBox.SelectionStart = logBox.Text.Length; logBox.ScrollToCaret(); }

        void CopySupportLog()
        {
            try { Clipboard.SetText(LogStore.Support(LogStore.Filter, Pending.Count)); Notify("Log copied. Paste it into your bug report.", Toast.Kind.Ok); }
            catch (Exception ex) { Notify("Couldn't copy the log: " + ex.Message, Toast.Kind.Bad); }
        }


        // ---- 3.2: first-run setup guide
        void RunSetupGuide()
        {
            // the first window starts a short service backup; the guide only ticks tweaks, so it can open meanwhile
            if (guideOpen) return;
            guideOpen = true;
            DialogResult r; SetupGuide g = new SetupGuide(rpCheck.Checked);
            try { r = g.ShowDialog(this); }
            finally { guideOpen = false; }
            using (g)
            {
                if (IsDisposed) return;   // window closed under the guide: ask again next start
                SetupGuide.MarkDone();
                rpCheck.Checked = g.RestorePoint || Mode.Lite;
                if (r == DialogResult.OK && g.Preset != null) SelectPreset(g.Preset);
                else Log.Say("Setup skipped. Pick tweaks on any page, or use 'Select the picks for this PC' on Home.");
            }
            if (!IsDisposed && Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "Mode") == null)   // 3.23: first start: Lite or Pro?
            {
                if (Dlg.Confirm("Lite or Pro mode?", L.T("Lite mode shows only safe tweaks and tools. It suits most people.") + "\n\n" + L.T("Pro mode shows every tweak and tool, including risky and experimental ones. It is for people who know how to repair Windows. You can switch at any time at the bottom of the sidebar."), "Use Pro mode…", "Use Lite mode"))
                    SwitchMode();
                else Mode.Set(false);
            }
            WhatsNew.MarkSeen();
            if (!Tour.Done && !IsDisposed) Tour.Show(this);   // 3.17: where things are, once after the setup guide
            if (rebuildAfterGuide) { rebuildAfterGuide = false; Rebuild(); }
        }

        // a short result the user should notice: status line (Log.Say) plus the pop-up
        public void Notify(string msg, Toast.Kind kind) { Log.Say("{0}", msg); toast.Show(L.T(msg), kind); }
    }
}
