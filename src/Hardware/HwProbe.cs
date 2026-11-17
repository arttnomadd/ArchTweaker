// ArchTweaker: hardware probe (1.5): CPU topology (P/E cores, SMT), network links, RAM rated vs configured, display refresh, VBS, timer state, Bluetooth radio.
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
    // ================= HARDWARE PROBE =================
    public class CpuTopo
    {
        public string Name = "";
        public int PCores, ECores, Threads;
        public ulong FastMask, AllMask;     // logical-processor bits (group 0) of the fastest cores / of every core
        public bool Smt, Hybrid;            // Hybrid = more than one efficiency class is ACTIVE right now
        public bool HybridModel;            // the CPU model is a hybrid design (12th gen Core and newer, Core Ultra)
        public bool ECoresDisabled { get { return HybridModel && !Hybrid; } }
        public int PhysicalFast { get { return PCores > 0 ? PCores : Math.Max(1, Threads / (Smt ? 2 : 1)); } }
    }

    public class LinkInfo { public string Name, Desc; public bool Wireless, Up; public long Mbps; }

    public static class HwProbe
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);

        static CpuTopo cpu;
        public static CpuTopo Cpu { get { if (cpu == null) cpu = ReadCpu(); return cpu; } }
        public static void Invalidate() { cpu = null; }

        static CpuTopo ReadCpu()
        {
            var t = new CpuTopo();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                    foreach (ManagementObject o in s.Get()) { t.Name = (o["Name"] ?? "").ToString().Trim(); break; }
            }
            catch { }
            t.Threads = Environment.ProcessorCount;
            var classes = new Dictionary<byte, int>();
            var masks = new Dictionary<byte, ulong>();
            try
            {
                uint len = 0;
                GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref len);          // RelationProcessorCore
                if (len > 0)
                {
                    IntPtr buf = Marshal.AllocHGlobal((int)len);
                    try
                    {
                        if (GetLogicalProcessorInformationEx(0, buf, ref len))
                        {
                            int off = 0;
                            while (off < len)
                            {
                                IntPtr rec = new IntPtr(buf.ToInt64() + off);
                                int size = Marshal.ReadInt32(rec, 4);
                                byte flags = Marshal.ReadByte(rec, 8);              // LTP_PC_SMT = 1
                                byte eff = Marshal.ReadByte(rec, 9);                // EfficiencyClass: higher = faster core
                                if ((flags & 1) != 0) t.Smt = true;
                                int c; classes.TryGetValue(eff, out c); classes[eff] = c + 1;
                                // GroupMask[0] (GROUP_AFFINITY) sits at offset 32: KAFFINITY Mask, WORD Group
                                if (size >= 32 + IntPtr.Size + 2 && Marshal.ReadInt16(rec, 32 + IntPtr.Size) == 0)
                                {
                                    ulong m = (ulong)Marshal.ReadIntPtr(rec, 32).ToInt64();
                                    ulong prev; masks.TryGetValue(eff, out prev); masks[eff] = prev | m;
                                }
                                if (size <= 0) break;
                                off += size;
                            }
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            catch { }
            if (classes.Count > 0)
            {
                byte top = classes.Keys.Max();
                t.PCores = classes[top];
                t.ECores = classes.Where(kv => kv.Key != top).Sum(kv => kv.Value);
                t.Hybrid = classes.Count > 1;
                ulong fm; masks.TryGetValue(top, out fm); t.FastMask = fm;
                foreach (var v in masks.Values) t.AllMask |= v;
            }
            string n = t.Name.ToLowerInvariant();
            t.HybridModel = t.Hybrid || IsHybridModel(n);
            return t;
        }

        // Intel models that ship with E-cores: Core Ultra, 12th–14th gen i7/i9, i5-13xxx/14xxx, i5-12600K/KF.
        // (i3 and i5-12400/12500/12600 non-K are P-core only.)
        public static bool IsHybridModel(string n)
        {
            n = n.ToLowerInvariant();
            if (n.Contains("core ultra") || n.Contains("core(tm) ultra")) return true;
            var rx = System.Text.RegularExpressions.Regex.Match(n, @"i([3579])-(1[234])(\d{2,3})([a-z]*)");
            if (!rx.Success) return false;
            string tier = rx.Groups[1].Value, gen = rx.Groups[2].Value, num = rx.Groups[3].Value, suf = rx.Groups[4].Value;
            if (tier == "7" || tier == "9") return true;
            if (tier == "5") return gen != "12" || (num.StartsWith("6") && suf.StartsWith("k"));
            return false;
        }

        public static List<LinkInfo> Links()
        {
            var l = new List<LinkInfo>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.NetworkInterfaceType != NetworkInterfaceType.Ethernet && ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211 && ni.NetworkInterfaceType != NetworkInterfaceType.GigabitEthernet) continue;
                    string d = ni.Description ?? "";
                    string dl = d.ToLowerInvariant();
                    if (dl.Contains("virtual") || dl.Contains("hyper-v") || dl.Contains("vpn") || dl.Contains("tap-") || dl.Contains("wireguard") || dl.Contains("loopback") || dl.Contains("bluetooth") || dl.Contains("wi-fi direct")) continue;
                    l.Add(new LinkInfo { Name = ni.Name, Desc = d, Wireless = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211, Up = ni.OperationalStatus == OperationalStatus.Up, Mbps = ni.OperationalStatus == OperationalStatus.Up ? ni.Speed / 1000000 : 0 });
                }
            }
            catch { }
            return l;
        }

        static IEnumerable<ManagementObject> Q(string q, string scope)
        {
            var list = new List<ManagementObject>();
            try { using (var s = new ManagementObjectSearcher(scope, q)) foreach (ManagementObject o in s.Get()) list.Add(o); } catch { }
            return list;
        }
        static int I(ManagementObject o, string p) { try { int v; return o[p] != null && int.TryParse(o[p].ToString(), out v) ? v : 0; } catch { return 0; } }

        // {configured, rated} MT/s of the first module
        public static int[] RamSpeed()
        {
            foreach (var o in Q("SELECT Speed,ConfiguredClockSpeed FROM Win32_PhysicalMemory", @"root\CIMV2")) return new[] { I(o, "ConfiguredClockSpeed"), I(o, "Speed") };
            return null;
        }

        public static string GpuName()
        {
            foreach (var o in Q("SELECT Name,AdapterCompatibility FROM Win32_VideoController", @"root\CIMV2"))
            {
                string n = (o["Name"] ?? "").ToString();
                if (n.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("Parsec", StringComparison.OrdinalIgnoreCase) < 0) return n;
            }
            return "";
        }

        // {current, max} refresh of the main display adapter
        public static int[] Refresh()
        {
            foreach (var o in Q("SELECT CurrentRefreshRate,MaxRefreshRate FROM Win32_VideoController", @"root\CIMV2"))
            {
                int c = I(o, "CurrentRefreshRate"), m = I(o, "MaxRefreshRate");
                if (c > 1) return new[] { c, m };
            }
            return null;
        }

        // true when VBS is actually RUNNING (not just configured)
        public static bool? VbsRunning()
        {
            foreach (var o in Q("SELECT VirtualizationBasedSecurityStatus FROM Win32_DeviceGuard", @"root\Microsoft\Windows\DeviceGuard"))
                return I(o, "VirtualizationBasedSecurityStatus") == 2;
            return null;
        }

        // 3.30: a Bluetooth radio Windows can see (built in or a USB dongle). null when WMI did not answer.
        static bool? bt; static bool btRead;
        public static bool? HasBluetooth()
        {
            if (btRead) return bt;
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\CIMV2", "SELECT DeviceID FROM Win32_PnPEntity WHERE PNPClass = 'Bluetooth'"))
                    bt = s.Get().Count > 0;
            }
            catch { bt = null; }
            btRead = true;
            return bt;
        }

        public static bool HasNvme()
        {
            return Reg.SubKeys(Hive.HKLM, @"SYSTEM\CurrentControlSet\Enum\NVME").Length > 0
                || Reg.SubKeys(Hive.HKLM, @"SYSTEM\CurrentControlSet\Enum\SCSI").Any(s => s.IndexOf("NVMe", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static string ActivePlanGuid()
        {
            var m = System.Text.RegularExpressions.Regex.Match(Shell.Run("powercfg.exe", "/getactivescheme", true), "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            return m.Success ? m.Value.ToLowerInvariant() : "";
        }
    }
}
