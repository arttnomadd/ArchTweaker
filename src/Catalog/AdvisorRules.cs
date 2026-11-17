// ArchTweaker: PC advisor rules added in 3.8.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace ArchTweaker
{
    // 3.9: hardware and software checks found in a real audit
    public static partial class Advisor
    {
        // A NIC driver from Windows itself (not oemNN.inf), or older than three years.
        public static string OldNicDriver(string desc, string inf, string date, DateTime now)
        {
            DateTime d; bool hasDate = DateTime.TryParseExact(date ?? "", new[] { "M-d-yyyy", "MM-dd-yyyy", "M/d/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
            bool inbox = !string.IsNullOrEmpty(inf) && !inf.StartsWith("oem", StringComparison.OrdinalIgnoreCase);
            bool old = hasDate && d < now.AddYears(-3);
            if (!inbox && !old) return null;
            string day = hasDate ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
            const string FIX = " Install the current driver from your board maker or Intel / Realtek. New drivers fix link drops and DPC spikes.";
            if (inbox && hasDate) return string.Format("{0} uses the driver built into Windows ({1}) from {2}." + FIX, desc, inf, day);
            if (inbox) return string.Format("{0} uses the driver built into Windows ({1})." + FIX, desc, inf);
            return string.Format("{0} uses a driver from {1}." + FIX, desc, day);
        }

        // [Framerate] Limit=N in an RTSS profile; 0 = no limit.
        public static int RtssLimit(string ini)
        {
            if (string.IsNullOrEmpty(ini)) return 0;
            bool sec = false;
            foreach (var raw in ini.Split('\n'))
            {
                string l = raw.Trim();
                if (l.StartsWith("[")) { sec = l.Equals("[Framerate]", StringComparison.OrdinalIgnoreCase); continue; }
                if (!sec || !l.StartsWith("Limit=", StringComparison.OrdinalIgnoreCase)) continue;
                int v; return int.TryParse(l.Substring(6).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v > 0 ? v : 0;
            }
            return 0;
        }

        static void Advisor39(List<Finding> f)
        {
            try
            {
                foreach (var k in Devices.PhysicalNicKeys())
                {
                    string msg = OldNicDriver((Reg.Get(Hive.HKLM, k, "DriverDesc") ?? "Network adapter").ToString(), (Reg.Get(Hive.HKLM, k, "InfPath") ?? "").ToString(), (Reg.Get(Hive.HKLM, k, "DriverDate") ?? "").ToString(), DateTime.Now);
                    if (msg != null) f.Add(Info("Old network adapter driver", msg, Level.Fix));
                }
            }
            catch (Exception ex) { Log.W("Advisor (NIC driver): {0}", ex.Message); }

            try
            {
                if (Process.GetProcessesByName("ProcessLasso").Length > 0 || Process.GetProcessesByName("ProcessGovernor").Length > 0)
                    f.Add(Info("Process Lasso is running", "Process Lasso can switch power plans by itself, which undoes the ArchTweaker power plan. In Process Lasso, turn off its power-plan automation (Options → Power). Keep ProBalance on and don't pin game affinities by hand.", Level.Tip));
            }
            catch { }

            try
            {
                string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string g = Path.Combine(pf86, @"RivaTuner Statistics Server\Profiles\Global");
                int lim = File.Exists(g) ? RtssLimit(File.ReadAllText(g)) : 0;
                var frl = Catalog.All.FirstOrDefault(t => t.Id == "gd_nv_drs_frl");
                if (lim > 0)
                    f.Add(Info("RivaTuner limits the frame rate", frl != null && StateCache.Applied(frl)
                        ? string.Format("RTSS caps every game at {0} fps, and the NVIDIA driver cap is also on. Two limiters add latency. Use G-Sync and V-Sync in the NVIDIA Control Panel with Reflex in the game. Keep the NVIDIA cap as the fallback and set the RTSS limit to 0.", lim)
                        : string.Format("RTSS caps every game at {0} fps. In-game limiters and Reflex give lower latency than RTSS (16 ms vs 18 ms in Blur Busters' 240 Hz tests). Use RTSS only for the overlay.", lim), Level.Tip));
            }
            catch { }

            var plm = Catalog.All.FirstOrDefault(t => t.Id == "svc_plm_manual");
            if (plm != null && plm.Applicable)
                f.Add(Make(Level.Tip, "Intel licensing service slows the boot", "Intel Platform License Manager starts at every boot and often times out after 45 s. Manual start removes the timeout.", new[] { "svc_plm_manual" }, "Intel Platform License Manager starts only when needed."));
        }
    }
}
