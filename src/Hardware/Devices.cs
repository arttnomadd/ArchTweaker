// ArchTweaker: Device lists: NICs, USB storage, laptop detection.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static partial class Devices
    {
        static readonly string[] VirtualNic = { "miniport", "vpn", "hyper-v", "virtual", "vmware", "virtualbox", "npcap", "wintun", "zerotier", "tailscale", "nordlynx", "tap-", "wireguard", "bluetooth", "loopback", "teredo" };
        public static bool IsVirtualNic(string desc)
        {
            string d = (desc ?? "").ToLowerInvariant();
            return VirtualNic.Any(v => d.Contains(v));
        }
        // Physical Ethernet / Wi-Fi adapters, without VPN, capture and virtual-switch drivers
        public static IEnumerable<string> PhysicalNicKeys()
        {
            return NicKeys().Where(k => !IsVirtualNic((Reg.Get(Hive.HKLM, k, "DriverDesc") ?? "").ToString()));
        }
        // MSI keys of USB host controllers and storage (NVMe / SATA) controllers whose driver offers MSI
        public static IEnumerable<string> UsbStorageMsiKeys()
        {
            return Irq.List().Where(d => (d.Kind == "USB" || d.Kind == "Storage") && d.HasMsiKey).Select(d => d.MsiKey);
        }
    }

    public static class Laptop
    {
        public static bool Is
        {
            get
            {
                try { var s = SystemInformation.PowerStatus.BatteryChargeStatus; return (s & BatteryChargeStatus.NoSystemBattery) == 0 && s != BatteryChargeStatus.Unknown; }
                catch { return false; }
            }
        }
    }

    public static partial class Devices
    {
        // RDNA3 / RDNA4 (RX 7000 / 9000 and the Radeon 7x0M / 8x0M / 8060S iGPUs): ULPS off hurts hardware GPU scheduling there
        static readonly Regex NewRadeon = new Regex(@"RX\s*[79]\d{3}|Radeon\s*(7[4-8]0M|8[4-9]0M|80[56]0S)", RegexOptions.IgnoreCase);
        public static bool IsNewRadeon(string desc) { return NewRadeon.IsMatch(desc ?? ""); }
        public static bool HasOldAmdGpu() { return AmdGpuKeys().Any(k => !IsNewRadeon((Reg.Get(Hive.HKLM, k, "DriverDesc") ?? "").ToString())); }
    }
}
