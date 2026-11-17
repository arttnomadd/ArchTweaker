// ArchTweaker: Interrupt devices: MSI mode, priority and affinity in the registry.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static partial class Devices
    {
        static readonly string[] NvTaskPrefixes = { "NvTmRep", "NvTmMon", "NvDriverUpdateCheckDaily", "NvProfileUpdaterDaily", "NvProfileUpdaterOnLogon", "NvBatteryBoostCheckOnLogon", "NvNodeLauncher" };
        static string[] nvTasks;
        // Task paths ("\NvTmRep_CrashReport1_{…}") as TaskOp expects them. Read once per start.
        public static string[] NvidiaTasks()
        {
            if (nvTasks != null) return nvTasks;
            var l = new List<string>();
            try
            {
                string dir = Path.Combine(Environment.SystemDirectory, "Tasks");
                foreach (var f in Directory.GetFiles(dir))
                {
                    string n = Path.GetFileName(f);
                    if (NvTaskPrefixes.Any(p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase))) l.Add("\\" + n);
                }
            }
            catch { }
            return nvTasks = l.ToArray();
        }
    }

    // ================= INTERRUPTS =================
    public class IrqDevice
    {
        public string Key;          // HKLM path of the Enum instance
        public string Name, Class, Service;
        public bool HasMsiKey, Msi;  // Msi = MSISupported is 1
        public int Policy;           // DevicePolicy: 0 = machine default … 4 = specified processors
        public ulong Mask;           // AssignmentSetOverride
        public bool Present;
        public string ImKey { get { return Key + @"\Device Parameters\Interrupt Management"; } }
        public string MsiKey { get { return ImKey + @"\MessageSignaledInterruptProperties"; } }
        public string AffKey { get { return ImKey + @"\Affinity Policy"; } }
        public int PinnedCpu { get { if (Policy != 4 || Mask == 0 || (Mask & (Mask - 1)) != 0) return -1; int i = 0; while ((Mask >> i & 1) == 0) i++; return i; } }
        public string Kind
        {
            get
            {
                string c = (Class ?? "").ToLowerInvariant();
                if (c == "display") return "Graphics";
                if (c == "net") return "Network";
                if (c == "usb") return "USB";
                if (c == "media") return "Audio";
                if (c == "scsiadapter" || c == "hdc") return "Storage";
                return Class ?? "";
            }
        }
    }

    public static class Irq
    {
        const string ENUM = @"SYSTEM\CurrentControlSet\Enum\PCI";
        static readonly string[] Interesting = { "display", "net", "usb", "media", "scsiadapter", "hdc" };

        public static string Clean(string desc)
        {
            if (string.IsNullOrEmpty(desc)) return "";
            int semi = desc.LastIndexOf(';');               // "@oem12.inf,%nvidia%;NVIDIA GeForce RTX 3080 Ti"
            return (semi >= 0 ? desc.Substring(semi + 1) : desc).Trim();
        }

        // 3.21.1: newer Windows builds (26H1 and some 24H2 installs) write only ClassGUID in the Enum key, no Class.
        static readonly Dictionary<string, string> ClassGuids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "{4d36e968-e325-11ce-bfc1-08002be10318}", "Display" }, { "{4d36e972-e325-11ce-bfc1-08002be10318}", "Net" },
            { "{36fc9e60-c465-11cf-8056-444553540000}", "USB" }, { "{4d36e96c-e325-11ce-bfc1-08002be10318}", "MEDIA" },
            { "{4d36e97b-e325-11ce-bfc1-08002be10318}", "SCSIAdapter" }, { "{4d36e96a-e325-11ce-bfc1-08002be10318}", "HDC" }
        };
        public static string ClassOf(string cls, string guid)
        {
            if (!string.IsNullOrEmpty(cls)) return cls;
            string c; return guid != null && ClassGuids.TryGetValue(guid.Trim(), out c) ? c : "";
        }

        // PCI devices that have an Interrupt Management key, the GPU first.
        public static List<IrqDevice> List()
        {
            var present = PresentIds();
            var l = new List<IrqDevice>();
            foreach (var dev in Reg.SubKeys(Hive.HKLM, ENUM))
                foreach (var inst in Reg.SubKeys(Hive.HKLM, ENUM + "\\" + dev))
                {
                    string k = ENUM + "\\" + dev + "\\" + inst;
                    string cls = ClassOf(Reg.Get(Hive.HKLM, k, "Class") as string, Reg.Get(Hive.HKLM, k, "ClassGUID") as string);
                    if (!Interesting.Contains(cls.ToLowerInvariant())) continue;
                    var d = new IrqDevice { Key = k, Class = cls, Service = (Reg.Get(Hive.HKLM, k, "Service") ?? "").ToString() };
                    d.Name = Clean((Reg.Get(Hive.HKLM, k, "FriendlyName") ?? Reg.Get(Hive.HKLM, k, "DeviceDesc") ?? dev).ToString());
                    d.Present = present == null || present.Contains(("PCI\\" + dev + "\\" + inst).ToUpperInvariant());
                    if (!d.Present) continue;
                    d.HasMsiKey = Reg.KeyExists(Hive.HKLM, d.MsiKey);
                    object m = Reg.Get(Hive.HKLM, d.MsiKey, "MSISupported"); d.Msi = m is int && (int)m == 1;
                    object p = Reg.Get(Hive.HKLM, d.AffKey, "DevicePolicy"); d.Policy = p is int ? (int)p : 0;
                    var mask = Reg.Get(Hive.HKLM, d.AffKey, "AssignmentSetOverride") as byte[];
                    if (mask != null) for (int i = 0; i < Math.Min(8, mask.Length); i++) d.Mask |= (ulong)mask[i] << (8 * i);
                    l.Add(d);
                }
            if (l.Count == 0) Log.W("Interrupt devices: none found ({0} PCI device keys, WMI present list {1}).", Reg.SubKeys(Hive.HKLM, ENUM).Length, present == null ? "unavailable" : present.Count.ToString());   // 3.21.1
            string[] order = { "Graphics", "Network", "Audio", "Storage", "USB" };
            return l.OrderBy(d => { int i = Array.IndexOf(order, d.Kind); return i < 0 ? 9 : i; }).ThenBy(d => d.Name).ToList();
        }

        // Device instance IDs Windows reports as present (so unplugged and old ghost devices are hidden).
        static HashSet<string> PresentIds()
        {
            try
            {
                var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var q = new ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'PCI%'"))
                    foreach (ManagementObject o in q.Get()) { var id = o["PNPDeviceID"] as string; if (id != null) s.Add(id.ToUpperInvariant()); }
                return s.Count > 0 ? s : null;
            }
            catch { return null; }
        }
    }
}
