// ArchTweaker: Restore point check: Volume Shadow Copy and System Protection, with a fix.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static class RestoreCheck
    {
        static int Start(string svc) { object v = Reg.Get(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\" + svc, "Start"); return v is int ? (int)v : -1; }

        // Null when restore points can work; otherwise what stops them.
        public static string Problem()
        {
            try
            {
                var off = new[] { "VSS", "swprv" }.Where(s => Start(s) == 4).ToList();
                if (off.Count > 0) return L.F("The {0} service is disabled. Restore points need it.", string.Join(" and ", off.ToArray()));
                object dis = Reg.Get(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableSR");
                if (dis is int && (int)dis == 1) return L.T("A policy turns System Restore off (DisableSR).");
                string sys = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
                var clients = Reg.Get(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients", "{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}") as string[];
                // the drive shows as "(C:)" or, on some builds, URL-encoded as "(C%3A)"
                string enc = "(" + sys.Replace(":", "%3A") + ")";
                if (clients == null || !clients.Any(c => c.IndexOf("(" + sys + ")", StringComparison.OrdinalIgnoreCase) >= 0 || c.IndexOf(enc, StringComparison.OrdinalIgnoreCase) >= 0))
                    return L.F("System Protection is off for {0}.", sys);
            }
            catch (Exception ex) { Log.W("Restore point check: {0}", ex.Message); return L.F("ArchTweaker could not check System Protection: {0}", ex.Message); }   // 3.27.1: not "fine"
            return null;
        }

        // Sets VSS and swprv to Manual, removes DisableSR, turns on System Protection for the system drive (up to 5 %).
        public static bool Fix() { string why; return Fix(out why); }
        // 3.27.1: true only when every step reports success and the check passes; 'why' says what failed.
        public static bool Fix(out string why)
        {
            why = null;
            try
            {
                foreach (var s in new[] { "VSS", "swprv" })
                    if (Start(s) == 4)
                    {
                        // through the Service Control Manager, so the change counts now (a registry write needs a restart)
                        string o = Shell.Run("sc.exe", "config " + s + " start= demand", true, 15000);
                        if (o.IndexOf("SUCCESS", StringComparison.OrdinalIgnoreCase) < 0) Reg.Set(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\" + s, "Start", RegistryValueKind.DWord, 3);
                    }
                if (Reg.Get(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableSR") != null) Reg.DeleteValue(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableSR");
                string r = Shell.PS("try { Enable-ComputerRestore -Drive \"$env:SystemDrive\\\" -ErrorAction Stop; "
                    + "& \"$env:windir\\System32\\vssadmin.exe\" resize shadowstorage /for=$env:SystemDrive /on=$env:SystemDrive /maxsize=5% | Out-Null; "
                    + "if ($LASTEXITCODE -ne 0) { 'OK (storage size not set: vssadmin exit code ' + $LASTEXITCODE + ')' } else { 'OK' } } catch { $_.Exception.Message }").Trim();
                Log.W("System Protection: {0}", r.Length == 0 ? "(no answer from PowerShell)" : r);
                if (!r.StartsWith("OK", StringComparison.Ordinal)) { why = r.Length == 0 ? L.T("PowerShell did not answer") : r; return false; }   // Windows sizes the storage itself when vssadmin cannot
                why = Problem();
                return why == null;
            }
            catch (Exception ex) { why = ex.Message; Log.W("System Protection: {0}", ex.Message); return false; }
        }
    }
}
