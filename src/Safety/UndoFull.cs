// ArchTweaker 1.7.2: "Undo everything" now also reverts changes the registry journal can't see
// (power plan, bcdedit/netsh/PowerShell commands, scheduled tasks, built-in actions such as the timer service).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ArchTweaker
{
    public static class FullUndo
    {
        // Parts of a tweak that live outside the registry journal.
        public static bool HasNonRegistryPart(Tweak t)
        {
            return t.Ops.Any(o => o is PowerOp || o is TaskOp || o is WmiPowerOp) || t.ApplyCmds.Count > 0 || t.RevertCmds.Count > 0 || t.ApplyExtra != null || t.RevertExtra != null;
        }

        // Only tweaks ArchTweaker itself applied (State is set by Tweak.Apply), latest-in-catalog first,
        // so settings Windows already had (e.g. TRIM on) are never switched off.
        public static List<Tweak> Candidates()
        {
            var l = Catalog.All.Where(t => State.Get(t.Id) && HasNonRegistryPart(t)).ToList();
            l.Reverse();
            return l;
        }

        public static void Everything()
        {
            var list = Candidates();
            if (list.Count > 0) Log.W("Reverting {0} tweak(s) that changed things outside the registry…", list.Count);
            PowerCache.Batch(() =>
            {
                foreach (var t in list)
                {
                    try { if (t.Applicable) { t.Revert(); Log.W("Reverted: {0}", t.Title); } }
                    catch (Exception ex) { Log.W("FAILED {0}: {1}", t.Title, ex.Message); }
                }
            });
            try { PowerExplorer.RevertAll(); } catch (Exception ex) { Log.W("Power settings revert: {0}", ex.Message); }
            try { PowerCache.Invalidate(); Probe.Invalidate(); } catch { }
            HostsModule.Remove();
            Journal.UndoAll();          // then every registry value back to its exact original
            RemoveLegacyProgramRules(); // after the replay, so nothing new is journaled
            AppliedSet.Clear();
            try { string h = Path.Combine(Paths.Root, "history.txt"); if (File.Exists(h)) File.Delete(h); } catch { }
        }

        // The "Program rules" page was removed in 2.2. Older versions could block an .exe or force its CPU
        // priority via Image File Execution Options; this removes any such rule they left behind (only keys
        // carrying the ArchTweakerRule marker), using raw deletes so no new undo entries are recorded.
        // 2.7.1: Game profiles were removed. Deletes its sign-in task and its game list if a 2.7 install created them.
        // Power plan / timer / affinity changes it made only lasted while a game ran, so nothing else needs undoing.
        public static void RemoveLegacyGameProfiles()
        {
            try
            {
                const string task = "ArchTweaker Game profiles";
                if (System.IO.File.Exists(System.IO.Path.Combine(Environment.SystemDirectory, "Tasks", task))   // 3.6.4: no schtasks process on every start
                    && Shell.Run("schtasks.exe", "/Query /TN \"" + task + "\"", true).IndexOf("ArchTweaker", StringComparison.OrdinalIgnoreCase) >= 0)
                    Log.W("Removed legacy task '{0}': {1}", task, Shell.Run("schtasks.exe", "/Delete /F /TN \"" + task + "\"", true).Trim());
                using (var b = Reg.Base(Hive.HKCU))
                    if (b.OpenSubKey(@"Software\ArchTweaker\GameProfiles") != null) { b.DeleteSubKeyTree(@"Software\ArchTweaker\GameProfiles", false); Log.W("Removed legacy game profile list."); }
            }
            catch (Exception ex) { Log.W("Legacy game-profile cleanup: {0}", ex.Message); }
        }

        public static int RemoveLegacyProgramRules()
        {
            const string IFEO = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\";
            const string MARK = "ArchTweakerRule";
            int n = 0;
            foreach (var exe in Reg.SubKeys(Hive.HKLM, IFEO))
            {
                string k = IFEO + exe;
                if (Reg.Get(Hive.HKLM, k, MARK) == null) continue;
                try
                {
                    using (var b = Reg.Base(Hive.HKLM))
                    using (var key = b.OpenSubKey(k, true))
                    {
                        if (key == null) continue;
                        key.DeleteValue("Debugger", false);
                        key.DeleteValue(MARK, false);
                        try { key.DeleteSubKeyTree("PerfOptions", false); } catch { }
                    }
                    if (Reg.ValueNames(Hive.HKLM, k).Length == 0 && Reg.SubKeys(Hive.HKLM, k).Length == 0) Reg.DeleteTreeRaw(Hive.HKLM, k);
                    Log.W("Removed old program rule for {0}", exe); n++;
                }
                catch (Exception ex) { Log.W("Could not remove old program rule for {0}: {1}", exe, ex.Message); }
            }
            return n;
        }
    }
}
