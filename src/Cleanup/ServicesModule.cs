// ArchTweaker: Services: list, start types, curated notes, .reg backup and restore.
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
    // ---------------- SERVICES ----------------
    public class SvcInfo
    {
        public string Name, Display, Status, Group, Note;
        public int Start; public bool Delayed;
    }

    public static class ServicesModule
    {
        // Group: Telemetry / Safe / Situational. Situational = only disable if you don't use the feature.
        public static readonly Dictionary<string, string[]> Known = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "DiagTrack", new[] { "Telemetry", "Connected User Experiences and Telemetry" } },
            { "dmwappushservice", new[] { "Telemetry", "WAP push message routing (telemetry)" } },
            { "diagnosticshub.standardcollector.service", new[] { "Telemetry", "Diagnostics Hub collector" } },
            { "WerSvc", new[] { "Telemetry", "Windows Error Reporting" } },
            { "RetailDemo", new[] { "Safe", "Store demo mode" } },
            { "MapsBroker", new[] { "Safe", "Offline maps downloader" } },
            { "RemoteRegistry", new[] { "Safe", "Remote registry editing (security)" } },
            { "Fax", new[] { "Safe", "Fax" } },
            { "WMPNetworkSvc", new[] { "Safe", "Windows Media Player sharing" } },
            { "lfsvc", new[] { "Situational", "Geolocation — needed for location features" } },
            { "SysMain", new[] { "Situational", "Superfetch — little benefit on NVMe" } },
            { "WSearch", new[] { "Situational", "Search indexer — slower file-content search if disabled" } },
            { "Spooler", new[] { "Situational", "Print spooler — needed for printers / PDF printing" } },
            { "PrintNotify", new[] { "Situational", "Printer extensions" } },
            { "bthserv", new[] { "Situational", "Bluetooth — needed for BT headsets/controllers" } },
            { "BTAGService", new[] { "Situational", "Bluetooth audio gateway" } },
            { "TabletInputService", new[] { "Situational", "Touch keyboard & handwriting — also emoji panel" } },
            { "WbioSrvc", new[] { "Situational", "Biometrics — needed for Windows Hello fingerprint/face" } },
            { "PhoneSvc", new[] { "Situational", "Phone service (Phone Link calls)" } },
            { "SCardSvr", new[] { "Situational", "Smart card" } },
            { "TermService", new[] { "Situational", "Remote Desktop host" } },
            { "SessionEnv", new[] { "Situational", "Remote Desktop configuration" } },
            { "XblAuthManager", new[] { "Situational", "Xbox Live auth — needed for Game Pass / Xbox app" } },
            { "XblGameSave", new[] { "Situational", "Xbox cloud saves" } },
            { "XboxNetApiSvc", new[] { "Situational", "Xbox Live networking" } },
            { "XboxGipSvc", new[] { "Situational", "Xbox accessory management" } },
            { "DoSvc", new[] { "Situational", "Delivery Optimization — Store downloads may slow" } },
            { "CscService", new[] { "Safe", "Offline Files" } },
            { "AJRouter", new[] { "Safe", "AllJoyn IoT router" } },
            { "WpcMonSvc", new[] { "Situational", "Parental controls" } },
            { "SEMgrSvc", new[] { "Safe", "NFC payments" } },
            { "icssvc", new[] { "Situational", "Mobile hotspot" } },
            { "wisvc", new[] { "Safe", "Windows Insider" } },
            { "DPS", new[] { "Situational", "Diagnostic Policy — needed by network troubleshooters" } },
        };

        static readonly string[] BtServices = { "bthserv", "BTAGService", "BthAvctpSvc" };
        public static List<SvcInfo> Load()
        {
            var list = new List<SvcInfo>();
            bool noBt = HwProbe.HasBluetooth() == false;   // 3.30: without a radio the Bluetooth services are safe to turn off
            foreach (var sc in ServiceController.GetServices())
            {
                try
                {
                    var s = new SvcInfo { Name = sc.ServiceName, Display = sc.DisplayName, Status = sc.Status.ToString() };
                    s.Start = Svc.GetStart(s.Name); s.Delayed = Svc.GetDelayed(s.Name);
                    string[] k;
                    if (Known.TryGetValue(s.Name, out k)) { s.Group = k[0]; s.Note = k[1]; } else { s.Group = ""; s.Note = ""; }
                    if (noBt && BtServices.Contains(s.Name, StringComparer.OrdinalIgnoreCase)) { s.Group = "Safe"; s.Note = "Bluetooth — this PC has no Bluetooth adapter"; }
                    list.Add(s);
                }
                catch { }
            }
            return list.OrderBy(x => x.Group == "" ? 1 : 0).ThenBy(x => x.Group).ThenBy(x => x.Display).ToList();
        }

        // Same idea as the original's "Services Backup": a .reg file with every service's Start value.
        public static string Backup(string file)
        {
            var sb = new StringBuilder("Windows Registry Editor Version 5.00\r\n\r\n");
            int n = 0;
            foreach (var name in Reg.SubKeys(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services"))
            {
                object st = Reg.Get(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\" + name, "Start");
                object type = Reg.Get(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\" + name, "Type");
                if (!(st is int) || !(type is int)) continue;
                if (((int)type & 0x30) == 0) continue; // only Win32 services, skip drivers
                sb.AppendFormat("[HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\{0}]\r\n\"Start\"=dword:{1:x8}\r\n", name, (int)st);
                object d = Reg.Get(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\" + name, "DelayedAutostart");
                if (d is int) sb.AppendFormat("\"DelayedAutostart\"=dword:{0:x8}\r\n", (int)d);
                sb.Append("\r\n"); n++;
            }
            File.WriteAllText(file, sb.ToString(), Encoding.Unicode);
            Log.W("Backed up {0} services to {1}", n, file);
            return file;
        }

        public static void Restore(string file)
        {
            string r = Shell.Run("reg.exe", "import \"" + file + "\" /reg:64", true);
            Log.W("Services restored from {0}. {1} Reboot to apply.", file, r.Trim());
        }

        public static string AutoBackupIfNone()
        {
            string f = Path.Combine(Paths.Backups, "services_original.reg");
            if (!File.Exists(f)) Backup(f);
            return f;
        }
    }
}
