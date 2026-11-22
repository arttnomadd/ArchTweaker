using System;
using System.Linq;
using System.Security.Principal;
using System.Windows.Forms;

// 2.7: targets .NET Framework 4.8 (in-box on Windows 10 1903+ and 11). The attribute turns on the 4.7.1–4.8
// behaviour: WinForms accessibility fixes (UIA patterns, focus cues, high-contrast), SystemDefault TLS, newer quirks.
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: System.Reflection.AssemblyTitle("ArchTweaker")]
[assembly: System.Reflection.AssemblyProduct("ArchTweaker")]
[assembly: System.Reflection.AssemblyDescription("Windows 10/11 tweaking, cleanup and gaming optimization utility")]
[assembly: System.Reflection.AssemblyVersion("3.40.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("3.40.0.0")]

namespace ArchTweaker
{
    public static class Program
    {
        public const string Version = "3.40.0";

        public static ApplicationContext Ctx;

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0].Equals("/gamewatch", StringComparison.OrdinalIgnoreCase))
                return 0;   // 2.7 game-profiles task (feature removed in 2.7.1): exit; startup cleanup deletes the task
            if (args.Length > 0 && args[0].Equals("/timerservice", StringComparison.OrdinalIgnoreCase))
            {
                System.ServiceProcess.ServiceBase.Run(new TimerResService());
                return 0;
            }
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Log.W("Unhandled: {0}", e.ExceptionObject);
                MessageBox.Show("ArchTweaker hit an error and has to close. Changes applied so far are in the undo journal, so Undo still works next time.\n\nLog: " + Paths.LogFile + "\n\n" + e.ExceptionObject, "ArchTweaker", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => { Log.W("UI error: {0}", e.Exception); Dlg.Error("Something went wrong", "ArchTweaker is still running. If this keeps happening, send the log: " + Paths.LogFile, e.Exception.Message); };

            try { System.Runtime.ProfileOptimization.SetProfileRoot(Paths.Root); System.Runtime.ProfileOptimization.StartProfile("startup.jitprofile"); } catch { }   // 3.6.4: faster starts after the first
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                MessageBox.Show(L.T("ArchTweaker needs administrator rights.\n\nRight-click ArchTweaker.exe and choose Run as administrator."), "ArchTweaker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }

            // 3.8: "/trace [exit]" writes a startup timeline; "/resume" is the logon task that continues a restart A/B test
            if (args.Any(x => x.Equals("/trace", StringComparison.OrdinalIgnoreCase)))
            {
                StartupTrace.Begin(args.Any(x => x.TrimStart('/').Equals("exit", StringComparison.OrdinalIgnoreCase)));
                args = args.Where(x => !x.Equals("/trace", StringComparison.OrdinalIgnoreCase) && !x.TrimStart('/').Equals("exit", StringComparison.OrdinalIgnoreCase)).ToArray();
            }
            if (args.Length > 0 && args[0].Equals("/resume", StringComparison.OrdinalIgnoreCase)) { AutoResume.Disarm(); args = new string[0]; }
            if (args.Length > 0) return Cli(args);

            Log.W("Starting ArchTweaker {0}", Version);
            // 3.32: once, off the UI thread: delete the large registry snapshots that versions before 3.31.1 wrote
            new System.Threading.Thread(() => RegSnapshot.CleanupLargeOnce()) { IsBackground = true }.Start();
            MainForm form;
            StartupTrace.Mark("admin check done");
            try { form = new MainForm(); StartupTrace.Mark("main window built"); }
            catch (Exception ex)
            {
                Log.W("Startup failed: {0}", ex);
                MessageBox.Show("ArchTweaker couldn't start.\n\nLog: " + Paths.LogFile + "\n\n" + ex, "ArchTweaker", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 4;
            }
            Ctx = new ApplicationContext(form);
            Application.Run(Ctx);
            return 0;
        }

        static int Cli(string[] args)
        {
            Log.Line += Console.WriteLine;
            var a = args.Select(x => x.TrimStart('/', '-').ToLowerInvariant()).ToList();
            try
            {
                if (a[0] == "preset" && args.Length > 1 && args[1].Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    var ids = Advisor.RecommendedIds().ToList();   // never contains Danger zone tweaks
                    foreach (var t in Catalog.All.Where(t => t.Applicable && !t.Dangerous && ids.Contains(t.Id, StringComparer.OrdinalIgnoreCase) && !t.IsApplied()))
                    { t.Apply(); Log.W("Applied: {0}", t.Title); }
                }
                else if (a[0] == "preset" && args.Length > 1)
                {
                    foreach (var t in Catalog.All.Where(t => t.Applicable && t.Presets.Contains(args[1], StringComparer.OrdinalIgnoreCase) && !t.IsApplied()))
                    { t.Apply(); Log.W("Applied: {0}", t.Title); }
                }
                else if (a[0] == "selftest")
                {
                    // 3.7: apply → check → undo → put back for every tweak that can be undone exactly; "/selftest read" changes nothing
                    var kind = args.Length > 1 && args[1].TrimStart('/', '-').Equals("read", StringComparison.OrdinalIgnoreCase) ? SelfTest.Kind.ReadOnly : SelfTest.Kind.Full;
                    int pass, fail, read; string file = SelfTest.RunToFile(kind, out pass, out fail, out read);
                    Log.W("Self-test: {0} passed, {1} failed, {2} read only. Report: {3}", pass, fail, read, file);
                    return fail == 0 ? 0 : 1;
                }
                else if (a[0] == "apply" && args.Length > 1)
                {
                    var ids = Profile.Read(args[1]).ToList();
                    // 3.4 (moved here in 3.5: it had landed on the auto-preset branch): Danger zone tweaks need /danger
                    bool danger = args.Any(x => x.TrimStart('/', '-').Equals("danger", StringComparison.OrdinalIgnoreCase));
                    foreach (var t in Catalog.All.Where(t => t.Applicable && ids.Contains(t.Id, StringComparer.OrdinalIgnoreCase) && !t.IsApplied()))
                    {
                        if (t.Dangerous && !danger) { Log.W("Skipped (Danger zone, add /danger to apply): {0}", t.Title); continue; }
                        var clash = Catalog.All.FirstOrDefault(x => t.Excludes.Contains(x.Id) && x.IsApplied());
                        if (clash != null) { Log.W("Skipped (can't be on together with '{0}', which is applied): {1}", clash.Title, t.Title); continue; }
                        if (RescueBackfill.Covers(t) && !RecoveryKit.Capture(t) && t.Dangerous) { Log.W("Skipped {0}: the recovery kit could not save the original values.", t.Title); continue; }   // 3.8; 3.27.1: no Danger tweak without its kit entry
                        t.Apply(); Log.W("Applied: {0}", t.Title);
                    }
                }
                else if (a[0] == "hostsupdate")
                {
                    // weekly task: refresh the selected lists only if the block is in use
                    if (HostsModule.IsActive()) HostsEngine.Update(HostsConfig.Load(), true, null);
                    else Log.W("Hosts block not active: nothing to update.");
                }
                else if (a[0] == "clean" && a.Contains("notify"))
                {
                    CleanSettings.Scheduled(true);   // weekly task: threshold + notification
                    return 0;
                }
                else if (a[0] == "clean")
                {
                    long freed = 0;
                    foreach (var t in CleanupModule.Targets().Where(t => t.Default)) freed += CleanupModule.Clean(t);
                    CleanSettings.MarkCleaned(freed);
                }
                else if (a[0] == "bootcheck") { BootGuard.OnStart(); return 0; }
                else if (a[0] == "devicepower") { WmiPowerOp.BootReapply(); return 0; }   // 3.8.2: boot task of gd_pcipower
                else if (a[0] == "bootok") { BootGuard.OnSignIn(); return 0; }
                else if (a[0] == "bootuser") { BootGuard.OnUserSignIn(); return 0; }
                else if (a[0] == "undo")
                {
                    FullUndo.Everything();
                }
                else if (a[0] == "reassert")
                {
                    if (args.Any(x => x.TrimStart('/', '-').Equals("danger", StringComparison.OrdinalIgnoreCase))) DangerZone.Unlocked = true;   // 3.5
                    string ap = HealthReport.ActiveProfile();
                    if (string.IsNullOrEmpty(ap) || !System.IO.File.Exists(ap)) { Log.W("No active profile set — nothing to re-assert."); }
                    else
                    {
                        var drift = Drift.Against(Profile.Read(ap).ToList());
                        Log.W("{0} tweak(s) drifted from {1}.", drift.Count, ap);
                        int n = Drift.Reassert(drift);
                        Log.W("Re-asserted {0} tweak(s).", n);
                    }
                }
                else if (a[0] == "deepclean")
                {
                    bool dry = a.Contains("dry-run") || a.Contains("dryrun");
                    long total = 0;
                    foreach (var t in CleanupModule.Targets().Where(t => t.Default)) { CleanupModule.Measure(t); total += dry ? Math.Max(0, t.Size) : CleanupModule.Clean(t); }
                    string st; var det = Winapp.Load(true, out st); Log.W(st);
                    foreach (var e in det.Where(e => e.Warning == null)) { long sz = Winapp.Measure(e); if (dry) total += sz; else { int sk; total += Winapp.Clean(e, out sk); } }
                    Log.W("Deep clean {0}: {1}", dry ? "would free" : "freed", CleanupModule.Fmt(total));
                }
                else if (a[0] == "restorepoint")
                {
                    Tools.RestorePoint();
                }
                else if (a[0] == "report")
                {
                    string f = HealthReport.Generate(true);
                    if (!a.Contains("silent")) Shell.OpenVisible("explorer.exe", "\"" + f + "\"");
                }
                else if (a[0] == "verify")
                {
                    string ap = HealthReport.ActiveProfile();
                    if (string.IsNullOrEmpty(ap) || !System.IO.File.Exists(ap)) Log.W("No active profile set — nothing to verify.");
                    else { var drift = Drift.Against(Profile.Read(ap).ToList()); Log.W("{0} of {1} profile tweak(s) not currently applied.", drift.Count, Profile.Read(ap).Count()); foreach (var d in drift) Log.W("  ! {0}", d.Tweak.Title); }
                }
                else if (a[0] == "measure")
                {
                    Log.W(Meter.Capture(200).Line());
                }
                else if (a[0] == "update")
                {
                    string err; var u = Updater.Check(out err);
                    if (u == null) Log.W("Update check failed: {0}", err);
                    else if (!u.Newer) Log.W("Up to date ({0}).", Version);
                    else { Log.W("Update available: {0}. {1}", u.Version, u.Notes); string e2 = Updater.Download(u); if (e2 != null) Log.W("Update failed: {0}", e2); }
                }
                else
                {
                    MessageBox.Show("Usage:\n  ArchTweaker.exe /preset Auto|Recommended|Gaming|\"Max Privacy\"\n  ArchTweaker.exe /apply profile.atp\n  ArchTweaker.exe /clean | /deepclean [/dry-run]\n  ArchTweaker.exe /reassert | /verify   (needs an active profile)\n  ArchTweaker.exe /report | /measure | /restorepoint\n  ArchTweaker.exe /hostsupdate | /update | /undo\n\nAdd /silent for no pop-ups.", "ArchTweaker");
                    return 2;
                }
                Native.BroadcastSettingChange();
                if (!a.Contains("silent")) MessageBox.Show(L.T("Done. Details are in the log: ") + Paths.LogFile, "ArchTweaker");
                return 0;
            }
            catch (Exception ex) { Log.W("Error: {0}", ex.Message); return 3; }
        }
    }
}
