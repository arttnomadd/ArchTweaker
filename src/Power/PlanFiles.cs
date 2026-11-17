// ArchTweaker: export / import power plans (.pow) (1.5).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ================= POWER PLAN FILES =================
    public static class PlanFiles
    {
        public static void ExportActive(string file)
        {
            string g = HwProbe.ActivePlanGuid();
            if (g.Length == 0) { Log.W("Could not read the active power plan."); return; }
            if (File.Exists(file)) File.Delete(file);
            string o = Shell.Run("powercfg.exe", "/export \"" + file + "\" " + g, true).Trim();
            Log.W(File.Exists(file) ? "Active plan exported to " + file : "Export failed: " + o);
        }

        // Imports with a fresh GUID so it never collides with an existing plan, then activates it.
        public static void Import(string file, bool activate)
        {
            string g = Guid.NewGuid().ToString();
            string o = Shell.Run("powercfg.exe", "/import \"" + file + "\" " + g, true).Trim();
            if (Shell.Run("powercfg.exe", "/list", true).IndexOf(g, StringComparison.OrdinalIgnoreCase) < 0) { Log.W("Import failed: " + o); return; }
            Log.W("Imported plan {0} from {1}", g, Path.GetFileName(file));
            if (activate) { Shell.Run("powercfg.exe", "/setactive " + g, true); PowerCache.Invalidate(); Log.W("Imported plan is now active."); }
        }
    }
}
