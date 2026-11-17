// ArchTweaker: System information readout.
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
    // ---------------- SYSTEM INFO ----------------
    public static class SysInfo
    {
        static IEnumerable<ManagementObject> Q(string q, string scope)
        {
            var list = new List<ManagementObject>();
            try { using (var s = new ManagementObjectSearcher(scope, q)) foreach (ManagementObject o in s.Get()) list.Add(o); } catch { }
            return list;
        }
        static IEnumerable<ManagementObject> Q(string q) { return Q(q, @"root\CIMV2"); }
        static string V(ManagementObject o, string p) { try { var x = o[p]; return x == null ? "" : x.ToString().Trim(); } catch { return ""; } }

        public static string Gather()
        {
            var sb = new StringBuilder();
            Action<string, string> L = (k, v) => sb.AppendLine((k.Length >= 22 ? k + "  " : k.PadRight(22)) + v);   // 3.14: a long name keeps a gap before its value
            L("OS", OSInfo.Name);
            foreach (var o in Q("SELECT LastBootUpTime FROM Win32_OperatingSystem"))
            {
                try { var up = DateTime.Now - ManagementDateTimeConverter.ToDateTime(V(o, "LastBootUpTime")); L("Uptime", string.Format("{0}d {1}h {2}m", up.Days, up.Hours, up.Minutes)); } catch { }
            }
            L("Computer", Environment.MachineName + "  (user: " + Environment.UserName + ")");
            sb.AppendLine();
            foreach (var o in Q("SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed FROM Win32_Processor"))
                L("CPU", V(o, "Name") + "  —  " + V(o, "NumberOfCores") + "C/" + V(o, "NumberOfLogicalProcessors") + "T, " + V(o, "MaxClockSpeed") + " MHz base");
            foreach (var o in Q("SELECT Manufacturer,Product FROM Win32_BaseBoard")) L("Motherboard", V(o, "Manufacturer") + " " + V(o, "Product"));
            foreach (var o in Q("SELECT SMBIOSBIOSVersion,ReleaseDate FROM Win32_BIOS"))
            {
                string d = V(o, "ReleaseDate"); L("BIOS", V(o, "SMBIOSBIOSVersion") + (d.Length >= 8 ? "  (" + d.Substring(0, 4) + "-" + d.Substring(4, 2) + "-" + d.Substring(6, 2) + ")" : ""));
            }
            long total = 0; var mods = new List<string>();
            foreach (var o in Q("SELECT Capacity,Speed,ConfiguredClockSpeed,Manufacturer,PartNumber,DeviceLocator FROM Win32_PhysicalMemory"))
            {
                long c; long.TryParse(V(o, "Capacity"), out c); total += c;
                mods.Add(string.Format("{0} {1} GB @ {2} MT/s (rated {3}) {4} {5}", V(o, "DeviceLocator"), c >> 30, V(o, "ConfiguredClockSpeed"), V(o, "Speed"), V(o, "Manufacturer"), V(o, "PartNumber")));
            }
            L("RAM", (total >> 30) + " GB in " + mods.Count + " module(s)");
            foreach (var m in mods) L("", m);
            if (mods.Count > 0) L("", "Tip: if configured speed < rated, enable XMP/EXPO in BIOS.");
            sb.AppendLine();
            foreach (var o in Q("SELECT Name,DriverVersion,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate FROM Win32_VideoController"))
            {
                L("GPU", V(o, "Name") + "  (driver " + V(o, "DriverVersion") + ")");
                if (V(o, "CurrentHorizontalResolution") != "") L("", "Display: " + V(o, "CurrentHorizontalResolution") + "x" + V(o, "CurrentVerticalResolution") + " @ " + V(o, "CurrentRefreshRate") + " Hz");
            }
            object hags = Reg.Get(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
            L("HAGS", hags is int ? ((int)hags == 2 ? "On" : "Off") : "Not set (driver default)");
            object gm = Reg.Get(Hive.HKCU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled");
            L("Game Mode", gm is int && (int)gm == 0 ? "Off" : "On");
            object vbs = Reg.Get(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");
            L("Memory Integrity", vbs is int && (int)vbs == 1 ? "On" : "Off / not configured");
            string plan = Shell.Run("powercfg.exe", "/getactivescheme", true).Trim();
            int i = plan.IndexOf('('); L("Power plan", i >= 0 ? plan.Substring(i).Trim('(', ')', ' ') : plan);
            sb.AppendLine();
            foreach (var o in Q("SELECT FriendlyName,MediaType,BusType,Size,HealthStatus FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
            {
                string mt = V(o, "MediaType") == "4" ? "SSD" : V(o, "MediaType") == "3" ? "HDD" : "?";
                string bus = V(o, "BusType") == "17" ? "NVMe" : V(o, "BusType") == "11" ? "SATA" : V(o, "BusType") == "7" ? "USB" : "bus " + V(o, "BusType");
                long sz; long.TryParse(V(o, "Size"), out sz);
                L("Disk", string.Format("{0}  —  {1} GB {2} {3}, health: {4}", V(o, "FriendlyName"), sz / 1000000000, bus, mt, V(o, "HealthStatus") == "0" ? "Healthy" : V(o, "HealthStatus")));
            }
            foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady && x.DriveType == DriveType.Fixed))
                L("Volume " + d.Name, CleanupModule.Fmt(d.AvailableFreeSpace) + " free of " + CleanupModule.Fmt(d.TotalSize) + "  (" + d.DriveFormat + ")");
            sb.AppendLine();
            foreach (var o in Q("SELECT Name,Speed FROM Win32_NetworkAdapter WHERE NetConnectionStatus=2"))
            {
                long sp; long.TryParse(V(o, "Speed"), out sp);
                L("Network", V(o, "Name") + (sp > 0 ? "  —  " + (sp / 1000000) + " Mbps link" : ""));
            }
            return sb.ToString();
        }
    }
}
