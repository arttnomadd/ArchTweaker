// ArchTweaker: Boot guard: undoes risky tweaks if Windows fails to start after them.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ================= BOOT SAFETY NET =================
    public static class BootGuard
    {
        const string KEY = @"SOFTWARE\ArchTweaker\BootGuard";
        public static readonly MaintTask Check = new MaintTask("bootcheck", "Boot check", "At every start: counts starts that don't reach the desktop.", "/SC ONSTART", "/bootcheck /silent", false);
        // SYSTEM at any sign-in (every user): the start reached the desktop.
        public static readonly MaintTask Ok = new MaintTask("bootok", "Boot OK", "At sign-in: marks the start as successful.", "/SC ONLOGON /DELAY 0000:45", "/bootok /silent", false);
        // the signed-in user: finishes a recovery (their own HKCU settings) and says what happened
        public static readonly MaintTask User = new MaintTask("bootuser", "Boot recovery", "At sign-in: finishes an automatic undo and explains it.", "/SC ONLOGON /DELAY 0001:00", "/bootuser /silent", true);

        public static bool Enabled
        {
            get { object o = Reg.Get(Hive.HKLM, KEY, "Enabled"); return o == null || o.ToString() != "0"; }
        }
        public static bool SetEnabled(bool on)
        {
            using (var b = Reg.Base(Hive.HKLM)) using (var k = b.CreateSubKey(KEY)) k.SetValue("Enabled", on ? 1 : 0, RegistryValueKind.DWord);
            if (on) return Maint.Install(Check) && Maint.Install(Ok) && Maint.Install(User);
            Maint.Remove(Check); Maint.Remove(Ok); Maint.Remove(User); Disarm(); return true;
        }
        static int Int(string n) { object o = Reg.Get(Hive.HKLM, KEY, n); return o is int ? (int)o : 0; }
        static void SetInt(string n, int v) { using (var b = Reg.Base(Hive.HKLM)) using (var k = b.CreateSubKey(KEY)) k.SetValue(n, v, RegistryValueKind.DWord); }

        // After an apply that needs a restart: watch the next starts.
        public static void Arm()
        {
            if (!Enabled) return;
            try
            {
                if (!Maint.Exists(Check)) Maint.Install(Check);
                if (!Maint.Exists(Ok)) Maint.Install(Ok);
                if (!Maint.Exists(User)) Maint.Install(User);
                SetInt("Armed", 1); SetInt("Count", 0); SetStr("LastStart", "");
                Log.W("Boot safety net armed for the next restart.");
            }
            catch (Exception ex) { Log.W("Boot safety net: {0}", ex.Message); }
        }
        public static void Disarm() { try { SetInt("Armed", 0); SetInt("Count", 0); } catch { } }
        public static bool Armed { get { return Int("Armed") == 1; } }

        static string Str(string n) { object o = Reg.Get(Hive.HKLM, KEY, n); return o == null ? "" : o.ToString(); }
        static void SetStr(string n, string v) { using (var b = Reg.Base(Hive.HKLM)) using (var k = b.CreateSubKey(KEY)) k.SetValue(n, v); }

        // Windows logs Kernel-Power 41 or EventLog 6008 at the next start when the previous one ended without a clean
        // shutdown (crash, blue screen, hang + reset, power cut). Clean restarts at the lock screen (Windows Update) don't.
        static bool UnexpectedShutdownSince(DateTime utc)
        {
            try
            {
                using (var log = new System.Diagnostics.EventLog("System"))
                {
                    var entries = log.Entries;
                    DateTime since = utc.ToLocalTime();
                    for (int i = entries.Count - 1, seen = 0; i >= 0 && seen < 20000; i--, seen++)
                    {
                        var e = entries[i];
                        if (e.TimeGenerated < since) break;   // newest first: everything older is before the last start
                        long id = e.InstanceId & 0xFFFF;
                        if ((id == 41 && e.Source.IndexOf("Kernel-Power", StringComparison.OrdinalIgnoreCase) >= 0) || (id == 6008 && e.Source == "EventLog")) return true;
                    }
                }
                return false;
            }
            catch (Exception ex) { Log.W("Boot check: event log not readable ({0}); counting the start.", ex.Message); return true; }
        }

        // /bootcheck (SYSTEM, at start). A start counts as failed only if the one before it never reached the desktop AND
        // ended in an unexpected shutdown. Two failed starts in a row → undo the last apply at this start.
        public static void OnStart()
        {
            if (Int("Armed") != 1) return;
            int n = Int("Count");
            DateTime last;
            if (n > 0 && DateTime.TryParse(Str("LastStart"), null, System.Globalization.DateTimeStyles.RoundtripKind, out last) && !UnexpectedShutdownSince(last))
                n = 0;   // the previous start shut down cleanly (e.g. an update restart at the lock screen): not a failure
            n++;
            SetInt("Count", n); SetStr("LastStart", DateTime.UtcNow.ToString("o"));
            Log.W("Boot check: start {0} since the last apply.", n);
            if (n < 3) return;
            Log.W("Windows did not reach the desktop twice after the last apply: undoing it.");
            UndoLast(false);
            SetInt("Recovered", 1); SetInt("Armed", 0); SetInt("Count", 0);
        }

        // /bootok (SYSTEM, at any user's sign-in): the start reached the desktop.
        public static void OnSignIn()
        {
            if (Int("Armed") == 1) { Disarm(); Log.W("Boot OK: Windows reached the desktop after the last apply."); }
        }

        // /bootuser (the signed-in user): after an automatic undo, also put back their own (HKCU) settings and explain.
        public static void OnUserSignIn()
        {
            if (Int("Recovered") != 1) return;
            UndoLast(true);
            SetInt("Recovered", 0);
            try { System.Windows.Forms.MessageBox.Show(L.T("Windows didn't finish starting twice after your last apply, so ArchTweaker undid that apply. Your PC is back to how it was before it."), "ArchTweaker"); } catch { }
        }

        static void UndoLast(bool pop)
        {
            var last = History.Last();
            if (last == null) return;
            foreach (var kv in Enumerable.Reverse(last))
            {
                try { if (kv.Value) kv.Key.Revert(); else kv.Key.Apply(); AppliedSet.Mark(kv.Key, !kv.Value); }
                catch (Exception ex) { Log.W("  boot undo {0}: {1}", kv.Key.Title, ex.Message); }
            }
            if (pop) History.Pop();
        }
    }
}
