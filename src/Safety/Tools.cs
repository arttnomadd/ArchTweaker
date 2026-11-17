// ArchTweaker: Repairs and Windows tools (SFC, DISM, TRIM, perf counters…).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ---------------- TOOLS ----------------
    public static class Tools
    {
        public static void RestorePoint()
        {
            Reg.Set(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "SystemRestorePointCreationFrequency", RegistryValueKind.DWord, 0);
            string r = Shell.PS("try { Checkpoint-Computer -Description 'ArchTweaker' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop; 'OK' } catch { $_.Exception.Message }");
            Log.W("Restore point: {0}", r.Trim().Length == 0 ? "done" : r.Trim() + (r.Contains("OK") ? "" : " (Is System Protection enabled for C:?)"));
        }
        public static void ExportDrivers(string folder)
        {
            Log.W("Exporting third-party drivers to {0} ...", folder);
            string r = Shell.Run("dism.exe", "/Online /Export-Driver /Destination:\"" + folder + "\"", true);
            Log.W(r.Contains("success") ? "Driver export finished." : r.Trim());
        }
        public static void ImportDrivers(string folder)
        {
            Log.W("Installing drivers from {0} ...", folder);
            string r = Shell.Run("pnputil.exe", "/add-driver \"" + folder.TrimEnd('\\') + "\\*.inf\" /subdirs /install", true);
            Log.W(r.Trim().Split('\n').LastOrDefault() ?? "done");
        }
        public static void RestartExplorer()
        {
            Shell.Run("taskkill.exe", "/f /im explorer.exe", true);
            Shell.OpenVisible("explorer.exe", "");
        }
        public static void OptimizeDrives() { Shell.OpenVisible("cmd.exe", "/k defrag.exe /C /O /U /V && echo. && echo Done."); }
        public static void RebuildPerfCounters() { PerfCounterFix.Run(); }   // 3.17.1: also clears "Disable Performance Counters" and checks the result
        public static void ClearStoreCache() { Shell.OpenVisible("wsreset.exe", ""); }
        public static void RebuildIconCache()
        {
            Shell.Run("ie4uinit.exe", "-show", true);
            Shell.Run("taskkill.exe", "/f /im explorer.exe", true);
            string dir = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer");
            try { foreach (var f in Directory.GetFiles(dir, "iconcache_*.db")) try { File.Delete(f); } catch { } } catch { }
            try { File.Delete(Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\IconCache.db")); } catch { }
            Shell.OpenVisible("explorer.exe", "");
            Log.W("Icon cache rebuilt.");
        }
        public static void ClearArp() { Log.W(Shell.Run("netsh.exe", "interface ip delete arpcache", true).Trim()); }
        public static void FlushDns() { Log.W(Shell.Run("ipconfig.exe", "/flushdns", true).Trim()); }
        public static void ResetNetwork()
        {
            Shell.Run("netsh.exe", "winsock reset", true);
            Shell.Run("netsh.exe", "int ip reset", true);
            Shell.Run("ipconfig.exe", "/flushdns", true);
            Log.W("Network stack reset. Reboot required.");
        }
        public static void ComponentCleanup()
        {
            Shell.OpenVisible("cmd.exe", "/k dism.exe /Online /Cleanup-Image /StartComponentCleanup && echo. && echo Done.");
        }
    }
}
