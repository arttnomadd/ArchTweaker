// ArchTweaker: Tools & backup page and profiles.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ================= HOSTS =================
    // ================= TOOLS =================
    public class ToolsPage : CardPage, IPage
    {
        // 2.10: Maintenance lives here too. Its page is built as usual and its groups are moved in,
        // so its handlers and labels keep working; the page object is kept for OnShow and Dispose.
        readonly MaintenancePage maint;
        public ToolsPage()
        {
            Heading("Maintenance", true);
            maint = new MaintenancePage();
            foreach (var c in maint.Controls.Cast<Control>().ToList()) Controls.Add(c);

            var guard = new SwitchBox { Text = "Boot safety net: undo the last apply if Windows fails to start twice after it", Checked = BootGuard.Enabled, AutoSize = true, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 2, 0, 2) };
            guard.CheckedChanged += delegate
            {
                bool want = guard.Checked, ok = true;
                MainForm.Instance.Busy(want ? "Turning on the boot safety net…" : "Turning off the boot safety net…", delegate { ok = BootGuard.SetEnabled(want); }, delegate
                { if (!ok) Dlg.Error("Couldn't create the boot check tasks", "Details are in the log: " + Paths.LogFile); });
            };
            var cleanTask = new SwitchBox { Text = "Weekly cleanup (Saturday 13:00) of the items ticked by default on the Cleanup page", Checked = Safe(() => Maint.Exists(CleanSettings.Task)), AutoSize = true, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 6, 0, 2) };
            cleanTask.CheckedChanged += delegate
            {
                bool want = cleanTask.Checked, ok = true;
                MainForm.Instance.Busy(want ? "Scheduling…" : "Removing task…", delegate { if (want) ok = Maint.Install(CleanSettings.Task); else Maint.Remove(CleanSettings.Task); },
                    delegate { if (!ok) Dlg.Error("Couldn't create the scheduled task", "Details are in the log: " + Paths.LogFile); });
            };
            var minRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(54, 2, 0, 2), BackColor = Theme.Panel };
            var minBox = new NumericUpDown { Minimum = 0, Maximum = 100000, Increment = 100, Value = Math.Max(0, Math.Min(100000, CleanSettings.MinMB)), Width = 90, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
            minBox.AccessibleName = "Minimum size in MB";
            minBox.ValueChanged += delegate { CleanSettings.MinMB = (int)minBox.Value; };
            minRow.Controls.Add(new Label { Text = "Only clean when there is more than", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 6, 6, 0), UseMnemonic = false });
            minRow.Controls.Add(minBox);
            minRow.Controls.Add(new Label { Text = "MB to free. A notification shows how much Cleanup freed.", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(6, 6, 0, 0), UseMnemonic = false });
            Group("Safety and cleanup", guard,
                new Label { Text = "After an apply that needs a restart, ArchTweaker counts the next starts. If Windows twice fails to reach the desktop, the apply is undone automatically at the next start.", ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = true, Margin = new Padding(54, 0, 0, 6), UseMnemonic = false },
                cleanTask, minRow);

            Heading("Backup & restore", false);
            Group("Apply history", Note("Each apply, newest first. Undo puts back one session and keeps every tweak that a later apply changed again. Point at a line to see all its changes."),
                new HistoryList());
            Group("Restore points and profiles", Note("A restore point lets Windows roll back system files and settings. A tweak profile (.atp) is the list of your applied tweaks, for this PC or another one."),
                Theme.Btn("Create restore point…", delegate { NamedRestorePoint(); }),   // 3.30: with your own name
                Theme.Btn("Export tweak profile…", delegate { ExportProfile(); }),
                Theme.Btn("Import tweak profile…", delegate { ImportProfile(); }),
                Theme.Btn("Open backup folder", delegate { Shell.OpenVisible("explorer.exe", "\"" + Paths.Root + "\""); }));
            Group("Drivers", Note("Save every third-party driver before a reinstall, and put them back from that folder later. Ghost devices are old hardware that Windows still keeps."),
                Theme.Btn("Export all third-party drivers…", delegate { string f = Folder("Choose a folder for exported drivers"); if (f != null) MainForm.Instance.Busy("Exporting drivers…", () => Tools.ExportDrivers(f), null); }),
                Theme.Btn("Install drivers from folder…", delegate { string f = Folder("Choose a folder containing .inf drivers"); if (f != null) MainForm.Instance.Busy("Installing drivers…", () => Tools.ImportDrivers(f), null); }),
                Theme.Btn("Ghost devices…", delegate { using (var g = new GhostDialog()) g.ShowDialog(FindForm()); }));
            // 3.6: NVIDIA Profile Inspector files and leftovers from other tweak packs
            Group("NVIDIA profile (.nip)", Note("Profile Inspector files. ArchTweaker imports only the global profile and backs up every value it changes first."),
                Theme.Btn("Import .nip into the global profile…", delegate { ImportNip(); }),
                Theme.Btn("Export the global profile (.nip)…", delegate { ExportNip(); }),
                Theme.Btn("Undo the last .nip import", delegate { string r = null; MainForm.Instance.Busy("Restoring NVIDIA settings…", () => { r = NipFiles.RestoreLast(); }, () => { Log.Say(r); MainForm.Instance.RefreshAll(); }); }));
            Group("Leftovers", Note("Finds values that other tweak packs left behind, explains each one and fixes the harmful ones."),
                Theme.Btn("Leftover tweaks from other packs…", delegate { using (var d = new PackLeftoverDialog()) d.ShowDialog(FindForm()); }));
            // 3.7: proves on this PC that every tweak that can be undone exactly applies, is detected and undoes
            // 3.8: Danger zone tweaks write a recovery kit first; 3.31: one card for the kit
            Group("Recovery kit", Note(L.F("Before a Pro or Danger zone tweak is applied, ArchTweaker saves its original values in {0}. If Windows does not start, open Command Prompt in Windows Recovery and run recovery.cmd from that folder. It puts the values back without Windows running. {1} tweak(s) are in the kit now.", RecoveryKit.Folder, RecoveryKit.Covered().Count())),
                Theme.Btn("Open the recovery kit folder", delegate
                {
                    if (System.IO.Directory.Exists(RecoveryKit.Folder)) Shell.OpenVisible("explorer.exe", "\"" + RecoveryKit.Folder + "\"");
                    else Dlg.Info("No recovery kit yet", L.F("When you apply a Danger zone tweak, ArchTweaker first writes {0} with recovery.cmd. If Windows then doesn't start, run it from Windows Recovery > Command Prompt to undo the tweak.", RecoveryKit.Folder));
                }),
                Theme.Btn("Update the recovery kit", delegate { int n = 0; MainForm.Instance.Busy("Updating the recovery kit…", () => { n = RescueBackfill.Run(true); }, () => Log.Say("Recovery kit: {0} tweak(s) added. {1} in the kit.", n, RecoveryKit.Covered().Count())); }));
            Heading("Fixes & diagnostics", false);
            Group("Self-test", Note("Checks on this PC that each tweak applies, shows its state and undoes exactly. The report opens when it's done."),
                Theme.Btn("Test tweaks: apply, check, undo", delegate { RunSelfTest(SelfTest.Kind.Full); }),
                Theme.Btn("Check tweak states only (changes nothing)", delegate { RunSelfTest(SelfTest.Kind.ReadOnly); }));
            Group("Fixes", Note("One-click Windows repairs. Reset network stack and Repair Windows take longer and can need a restart. Repair Windows runs ChkDsk, SFC, DISM and SFC again, and skips a step that runs too long. Install game runtimes uses winget."),
                Theme.Btn("Restart Explorer", delegate { Tools.RestartExplorer(); }),
                Theme.Btn("Flush DNS", delegate { Tools.FlushDns(); }),
                Theme.Btn("Clear ARP cache", delegate { Tools.ClearArp(); }),
                Theme.Btn("Rebuild icon cache", delegate { Tools.RebuildIconCache(); }),
                Theme.Btn("Clear Store cache (wsreset)", delegate { Tools.ClearStoreCache(); }),
                Theme.Btn("Reset network stack", delegate { if (Dlg.Confirm("Reset the network stack?", "Resets Winsock and TCP/IP to Windows defaults. A static IP or custom DNS may need to be set again. Restart Windows afterwards.", "Reset network")) MainForm.Instance.Busy("Resetting network…", Tools.ResetNetwork, null); }),
                Theme.Btn("Repair Windows (ChkDsk, SFC, DISM, SFC)", delegate { RepairChain(); }),   // 3.30
                Theme.Btn("Repair WMI", delegate { string r = null; MainForm.Instance.Busy("Checking the WMI repository…", () => { r = Repairs.RepairWmi(); }, () => Log.Say(r)); }),   // 3.30
                Theme.Btn("Install game runtimes (Visual C++, DirectX, .NET)", delegate { InstallRuntimes(); }),   // 3.30
                Theme.Btn("Rebuild performance counters", delegate { MainForm.Instance.Busy("Rebuilding counters…", Tools.RebuildPerfCounters, null); }),
                Theme.Btn("Turn on System Protection", delegate { bool ok = false; string why = null; MainForm.Instance.Busy("Turning on System Protection…", () => { ok = RestoreCheck.Fix(out why); }, () => { if (ok) Log.Say("System Protection is on. Restore points work now."); else Log.Say("System Protection is still off: {0}", why ?? L.T("check the log")); }); }));   // 3.24
            Group("Performance", Note("Read-only checks and Windows' own tools. The results go to the log."),
                Theme.Btn("Show current timer resolution", delegate { Log.W(TimerRes.Query()); }),
                Theme.Btn("Foreground boost value…", delegate { using (var d = new ForegroundBoostDialog()) d.ShowDialog(FindForm()); }),   // 3.30
                Theme.Btn("Optimize drives (TRIM / defrag)", delegate { Tools.OptimizeDrives(); }),
                Theme.Btn("Open advanced power settings", delegate { Shell.OpenVisible("control.exe", "powercfg.cpl,,3"); }),
                Theme.Btn("Check sleep states (powercfg /a)", delegate { Log.W(Shell.Run("powercfg.exe", "/a", true).Trim()); }),
                Theme.Btn("Measure Sleep(1) precision", delegate { string r = null; MainForm.Instance.Busy("Measuring Sleep(1) × 500…", () => { r = LatencyTest.SleepPrecision(500); }, () => Log.W(r)); }));
            Group("Power plan files", Note("Back up, share or compare power plans as .pow files."),
                Theme.Btn("Export active plan (.pow)…", delegate { ExportPlan(); }),
                Theme.Btn("Import plan (.pow) and activate…", delegate { ImportPlan(); }),
                Theme.Btn("Compare a .pow with the active plan…", delegate { ComparePlan(); }),
                Theme.Btn("List all plans", delegate { Log.W(Shell.Run("powercfg.exe", "/list", true).Trim()); }));
            Heading("Shortcuts", false);
            if (Mode.Pro)
                Group("Pro tools", Note("Export writes the ticked tweaks as a .reg file and a .cmd file. With nothing ticked, it writes the applied ones. Command line: ArchTweaker.exe /preset name · /apply profile.atp · /clean · /deepclean · /verify · /reassert · /report · /measure · /restorepoint · /undo. Add /silent for no pop-ups."),
                    Theme.Btn("Export tweaks as .reg / .cmd…", delegate
                    {
                        var p = MainForm.Instance.Pending.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
                        if (p.Count == 0) { var ids = new HashSet<string>(AppliedSet.Read()); p = Catalog.All.Where(t => ids.Contains(t.Id)).ToList(); }
                        ProExport.Run(FindForm(), p);
                    }));
            Group("Windows shortcuts",
                Theme.Btn("Open Services", delegate { Shell.OpenVisible("services.msc", ""); }),
                Theme.Btn("Open Device Manager", delegate { Shell.OpenVisible("devmgmt.msc", ""); }),
                Theme.Btn("Open Power Options", delegate { Shell.OpenVisible("powercfg.cpl", ""); }),
                Theme.Btn("Open Graphics settings", delegate { Shell.OpenVisible("ms-settings:display-advancedgraphics", ""); }),
                Theme.Btn("Open Registry Editor", delegate { Shell.OpenVisible("regedit.exe", ""); }));
        }
        // 3.30
        void RepairChain()
        {
            if (!Dlg.Confirm("Repair Windows?", "ArchTweaker runs a ChkDsk online scan, SFC, DISM RestoreHealth and SFC again, one after the other. This can take an hour. DISM downloads files from Windows Update. Windows asks before it shuts down while the repair runs. Each step stops after its time limit and the next one starts.", "Start the repair")) return;
            List<string> res = null;
            MainForm.Instance.Busy("Repairing Windows…", () => { res = Repairs.RunChain(MainForm.Instance.BusyStatus); }, () =>
            {
                if (res == null) return;
                foreach (var l in res) Log.W(l);
                Dlg.Info("Repair finished", "Restart Windows so the repaired files are used. The result of each step is in the log.", string.Join("\r\n", res));
            });
        }
        void InstallRuntimes()
        {
            if (Repairs.Winget() == null) { Dlg.Error("winget is not installed", "Install App Installer from the Microsoft Store, then try again."); return; }
            if (!Dlg.Confirm("Install game runtimes?", "A console window uses winget to install runtimes from Microsoft. These are Visual C++ 2005 to 2022 (x86 and x64), DirectX, and the .NET 8 and .NET 10 desktop runtimes. It skips runtimes that you already have.", "Install")) return;
            Repairs.InstallRuntimes();
        }
        void NamedRestorePoint()
        {
            string n = TextDialog.Show(FindForm(), "Create restore point", "Name of the restore point. Windows shows this name in System Restore.", "ArchTweaker " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            if (n == null) return;
            string r = null;
            MainForm.Instance.Busy("Creating restore point…", () => { r = Repairs.NamedRestorePoint(n); }, () => Log.Say(r));
        }
        protected override void Dispose(bool disposing) { if (disposing && maint != null) maint.Dispose(); base.Dispose(disposing); }
        static string Folder(string desc)
        {
            using (var d = new FolderBrowserDialog { Description = desc, ShowNewFolderButton = true })
                return d.ShowDialog() == DialogResult.OK ? d.SelectedPath : null;
        }
        void ExportPlan()
        {
            using (var d = new SaveFileDialog { Filter = "Power plan|*.pow", FileName = Environment.MachineName + "-plan-" + DateTime.Now.ToString("yyyyMMdd") + ".pow" })
                if (d.ShowDialog() == DialogResult.OK) { string f = d.FileName; MainForm.Instance.Busy("Exporting power plan…", () => PlanFiles.ExportActive(f), null); }
        }
        void ComparePlan()
        {
            using (var d = new OpenFileDialog { Filter = "Power plan|*.pow|All files|*.*" })
                if (d.ShowDialog() == DialogResult.OK)
                {
                    string f = d.FileName, r = null;
                    MainForm.Instance.Busy("Comparing power plans…", () => { r = PlanCompare.Run(f); }, () => Dlg.Info("Power plan comparison", Path.GetFileName(f) + " vs the active plan", r));
                }
        }
        void RunSelfTest(SelfTest.Kind kind)
        {
            if (kind == SelfTest.Kind.Full && !Dlg.Confirm("Run the tweak self-test?", "ArchTweaker applies, checks and undoes each tweak that is not applied yet and has an exact undo, one at a time. Then it writes back the original values. This covers registry, services, scheduled tasks, power plan, NVIDIA profile and device registry tweaks. It only reads tweaks that run commands, disable devices or change network adapters. It skips Danger zone tweaks. It takes a few minutes, so close games first.", "Run self-test")) return;
            int pass = 0, fail = 0, read = 0; string file = null;
            MainForm.Instance.Busy(kind == SelfTest.Kind.Full ? "Testing tweaks…" : "Reading tweak states…", () => { file = SelfTest.RunToFile(kind, out pass, out fail, out read); }, () =>
            {
                Log.Say("Self-test: {0} passed, {1} failed, {2} read only. Report: {3}", pass, fail, read, file);
                MainForm.Instance.Notify(fail == 0 ? L.F("Self-test: {0} passed, {1} read only. Report saved.", pass, read) : L.F("Self-test: {0} failed. See the report.", fail), fail == 0 ? Toast.Kind.Ok : Toast.Kind.Warn);
                Shell.OpenVisible("notepad.exe", "\"" + file + "\"");
                MainForm.Instance.RefreshAll();
            });
        }

        void ImportNip()
        {
            if (!NvDrs.Available) { Dlg.Info("NVIDIA driver not found", "Importing a .nip file needs an NVIDIA GPU with its driver installed."); return; }
            using (var d = new OpenFileDialog { Filter = "NVIDIA Profile Inspector|*.nip|All files|*.*" })
                if (d.ShowDialog() == DialogResult.OK)
                {
                    string f = d.FileName, r = null;
                    if (!Dlg.Confirm("Import " + Path.GetFileName(f) + "?", "ArchTweaker imports only the global (Base Profile) settings and skips per-game profiles. It backs up every value it changes first, and 'Undo the last .nip import' puts them back.", "Import")) return;
                    MainForm.Instance.Busy("Importing NVIDIA settings…", () => { r = NipFiles.Import(f); }, () => { Log.Say(r); MainForm.Instance.RefreshAll(); });
                }
        }
        void ExportNip()
        {
            if (!NvDrs.Available) { Dlg.Info("NVIDIA driver not found", "Exporting needs an NVIDIA GPU with its driver installed."); return; }
            using (var d = new SaveFileDialog { Filter = "NVIDIA Profile Inspector|*.nip", FileName = "archtweaker-global.nip" })
                if (d.ShowDialog() == DialogResult.OK) { int n = NipFiles.Export(d.FileName); Log.Say("{0} NVIDIA settings exported to {1}", n, d.FileName); }
        }
        void ImportPlan()
        {
            using (var d = new OpenFileDialog { Filter = "Power plan|*.pow|All files|*.*" })
                if (d.ShowDialog() == DialogResult.OK) { string f = d.FileName; MainForm.Instance.Busy("Importing power plan…", () => PlanFiles.Import(f, true), () => MainForm.Instance.RefreshTweaks()); }
        }
        void ExportProfile()
        {
            using (var d = new SaveFileDialog { Filter = "ArchTweaker profile|*.atp", FileName = "my-tweaks.atp" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                var ids = Catalog.All.Where(t => t.Applicable && t.IsApplied()).Select(t => t.Id).ToList();
                File.WriteAllLines(d.FileName, new[] { "# ArchTweaker profile " + DateTime.Now.ToString("yyyy-MM-dd") + " — " + OSInfo.Name, "#hw: " + HwFingerprint.Current() }.Concat(ids));
                Log.W("Exported {0} applied tweaks to {1}", ids.Count, d.FileName);
            }
        }
        void ImportProfile()
        {
            using (var d = new OpenFileDialog { Filter = "ArchTweaker profile|*.atp|All files|*.*" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                try
                {
                    string hw = File.ReadAllLines(d.FileName).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("#hw:"));
                    if (hw != null) { string diff = HwFingerprint.Diff(hw.Substring(4).Trim()); if (diff != null) Dlg.Info("This profile comes from different hardware", "Some tweaks may not apply or may not suit this PC. Review them before you apply.", diff); }
                }
                catch { }
                MainForm.Instance.SelectIds(Profile.Read(d.FileName));
                Log.W("Profile loaded. Review the selected tweaks, then click Apply.");
            }
        }
        public void OnShow() { maint.OnShow(); }
        public void Filter(string q) { }
    }

    public static class Profile
    {
        public static IEnumerable<string> Read(string file)
        {
            // 3.6.1: ids of merged tweaks are read as the tweak that replaced them
            return File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).Select(Catalog.CurrentId).Distinct(StringComparer.OrdinalIgnoreCase);
        }
    }
}
