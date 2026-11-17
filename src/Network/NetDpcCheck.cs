// ArchTweaker: Network adapter DPC check.
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
    // 3.9: network adapter tweaks change how the NIC driver raises DPCs. After the next start, a 10 s scan shows
    // whether a network driver now has DPC/ISR spikes of 500 µs or more.
    public static class NetDpcCheck
    {
        public static readonly string[] Ids = { "net_nicadv", "net_itr_off", "net_nicpower2", "gd_nicpower", "gd_wifi_roam" };
        const string KEY = @"Software\ArchTweaker", VAL = "NetDpcCheck";
        static readonly Regex Net = new Regex(@"^(ndis|tcpip|netio|e1[dirxq]\w*|e2f\w*|rt6[48]\w*|rt68\w*|rtux\w*|netwtw\w*|netwbw\w*|athw\w*|bcmwl\w*|kille\w*|mtkwl\w*|rtwlan\w*|vwifi\w*|ixgb\w*|mlx\w*)\.sys$", RegexOptions.IgnoreCase);
        public static bool IsNetDriver(string d) { return Net.IsMatch(d ?? ""); }

        public static void Arm(IEnumerable<Tweak> applied)
        {
            if (applied.Any(t => Ids.Contains(t.Id))) Reg.SetRaw(Hive.HKCU, KEY, VAL, RegistryValueKind.DWord, 1);
        }

        // The message, or null when every network driver stays under 500 µs.
        public static string Verdict(IEnumerable<DriverStat> stats)
        {
            var bad = stats.Where(x => IsNetDriver(x.Driver) && x.MaxUs >= 500).OrderByDescending(x => x.MaxUs).ToList();
            if (bad.Count == 0) return null;
            var w = bad[0];
            return string.Format("After your network tweaks, {0} has DPC/ISR spikes up to {1} µs. Check it on the Latency page. If it stays high, revert 'Network adapter: interrupt moderation off' or the other network tweaks one at a time.", w.Driver, (int)w.MaxUs);
        }

        public static void RunLater(Action<string> notify)
        {
            object v = Reg.Get(Hive.HKCU, KEY, VAL);
            if (!(v is int) || (int)v != 1) return;
            Reg.DeleteValueRaw(Hive.HKCU, KEY, VAL);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Thread.Sleep(60000);   // let start-up work finish first
                    if (DriverTrace.Busy) return;
                    string err; var stats = DriverTrace.Run(10, out err);
                    if (stats == null) { Log.W("Network DPC check: {0}", err); return; }
                    string msg = Verdict(stats);
                    Log.W("Network DPC check: {0}", msg ?? "network drivers stay under 500 µs");
                    if (msg != null) notify(msg);
                }
                catch (Exception ex) { Log.W("Network DPC check: {0}", ex.Message); }
            });
        }
    }
}
