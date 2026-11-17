// ArchTweaker: Network test: latency and jitter to a few hosts.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ---------------- network test: ping and jitter to the router and to the internet ----------------
    public static class NetTest
    {
        public class Stat
        {
            public string Target; public int Sent, Lost; public double Avg, Min, Max, Jitter;
            public double LossPct { get { return Sent == 0 ? 100 : 100.0 * Lost / Sent; } }
            public string Line() { return Sent == Lost ? string.Format("{0}: no reply", Target) : string.Format("{0}: avg {1:0.0} ms · min {2:0.0} · max {3:0.0} · jitter {4:0.0} ms · loss {5:0.#} %", Target, Avg, Min, Max, Jitter, LossPct); }
        }
        // rtt null = lost. Jitter = mean change between consecutive replies.
        public static Stat Compute(string target, IList<double?> rtts)
        {
            var s = new Stat { Target = target, Sent = rtts.Count, Lost = rtts.Count(r => !r.HasValue) };
            var ok = rtts.Where(r => r.HasValue).Select(r => r.Value).ToList();
            if (ok.Count == 0) return s;
            s.Avg = ok.Average(); s.Min = ok.Min(); s.Max = ok.Max();
            if (ok.Count > 1) { double sum = 0; for (int i = 1; i < ok.Count; i++) sum += Math.Abs(ok[i] - ok[i - 1]); s.Jitter = sum / (ok.Count - 1); }
            return s;
        }
        public static Stat Run(string target, int count, int intervalMs)
        {
            var r = new List<double?>();
            using (var p = new Ping())
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var rep = p.Send(target, 1000);
                        sw.Stop();
                        // RoundtripTime is whole ms; the stopwatch gives sub-ms on a LAN
                        r.Add(rep.Status == IPStatus.Success ? (double?)Math.Min(sw.Elapsed.TotalMilliseconds, Math.Max(1, rep.RoundtripTime) + 1) : null);
                    }
                    catch { r.Add(null); }
                    System.Threading.Thread.Sleep(intervalMs);
                }
            return Compute(target, r);
        }
        public static string Gateway()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
                    foreach (var g in ni.GetIPProperties().GatewayAddresses)
                        if (g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !g.Address.ToString().StartsWith("0.")) return g.Address.ToString();
            }
            catch { }
            return null;
        }
        // where the problem is: between the PC and the router, or past the router
        public static string Verdict(Stat router, Stat net)
        {
            if (router != null && (router.LossPct > 0 || router.Jitter > 2 || router.Avg > 5))
                return "BAD: unstable between this PC and the router. Check Wi-Fi signal, the cable and the adapter's power saving (Internet & Network, GPU & Devices).";
            if (net.Sent == net.Lost) return "No reply from the internet test address. A firewall can block ping. The router test above still counts.";
            if (net.LossPct >= 1 || net.Jitter > 8)
                return "BAD: the connection is unstable past the router (provider, or bufferbloat when the line is busy). Run a bufferbloat test while downloading, and turn on SQM / QoS in the router if it has it.";
            return "GOOD: low jitter and no loss to the router and to the internet.";
        }
    }
}
