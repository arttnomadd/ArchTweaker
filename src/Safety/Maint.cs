// ArchTweaker 2.0: maintenance scheduler. Registers Scheduled Tasks that run ArchTweaker headlessly
// to re-assert the active profile, deep-clean, and take restore points. Mirrors the hosts weekly-task pattern.
using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ArchTweaker
{
    public class MaintTask
    {
        public string Id, Name, Desc, Schedule, Cli;
        public bool AsUser;         // true = run as the interactive user (profile/HKCU tweaks); false = SYSTEM
        public MaintTask(string id, string name, string desc, string sched, string cli, bool asUser)
        { Id = id; Name = name; Desc = desc; Schedule = sched; Cli = cli; AsUser = asUser; }
        public string TaskName { get { return "ArchTweaker " + Name; } }
    }

    public static class Maint
    {
        public static readonly MaintTask[] Tasks =
        {
            // re-assert touches the user's profile + HKCU, so it must run as the interactive user, not SYSTEM
            new MaintTask("reassert", "Re-assert profile", "Weekly: re-applies any tweaks from your active profile that Windows has reset.",
                "/SC WEEKLY /D SUN /ST 12:30", "/reassert /silent", true),
            new MaintTask("deepclean", "Deep clean", "Monthly: removes temp/cache/log leftovers using the saved Winapp2 rules.",
                "/SC MONTHLY /D 1 /ST 03:00", "/deepclean /silent", false),
            new MaintTask("restorepoint", "Weekly restore point", "Weekly: creates a System Restore point so you always have a recent rollback.",
                "/SC WEEKLY /D WED /ST 11:00", "/restorepoint /silent", false),
        };

        public static string InstalledExe { get { return Path.Combine(Paths.Root, @"bin\ArchTweaker.exe"); } }

        // Create (or re-secure) the bin folder so only SYSTEM and Administrators can write it. Without this,
        // a standard user who pre-created the folder could plant an exe that a SYSTEM task would then run.
        static bool EnsureExe() { return Secure.InstallExe() != null; }   // 3.2.1: one hardened installer for every SYSTEM task
        public static bool Exists(MaintTask t)
        {
            return Shell.Run("schtasks.exe", "/Query /TN \"" + t.TaskName + "\"", true).IndexOf("ArchTweaker", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Returns true if the task now exists.
        public static bool Install(MaintTask t)
        {
            if (!EnsureExe()) { Log.W("Scheduling '{0}' aborted: could not stage a secured copy of the exe.", t.TaskName); return false; }
            string tr = "\\\"" + InstalledExe + "\\\" " + t.Cli;
            string principal = t.AsUser ? "/IT" : "/RU SYSTEM";
            string o = Shell.Run("schtasks.exe", "/Create /F /TN \"" + t.TaskName + "\" " + t.Schedule + " /RL HIGHEST " + principal + " /TR \"" + tr + "\"", true).Trim();
            bool ok = Exists(t);
            Log.W("Scheduled '{0}': {1}", t.TaskName, ok ? "created" : ("FAILED — " + (o.Length > 0 ? o : "unknown error")));
            return ok;
        }

        public static void Remove(MaintTask t)
        {
            string o = Shell.Run("schtasks.exe", "/Delete /F /TN \"" + t.TaskName + "\"", true).Trim();
            Log.W("Removed '{0}': {1}", t.TaskName, o.Length > 0 ? o : "done");
        }
    }
}
