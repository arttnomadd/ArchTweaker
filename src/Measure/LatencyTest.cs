// ArchTweaker: Sleep(1) precision measurement (1.5, MeasureSleep-style).
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
    // ================= LATENCY TEST =================
    public static class LatencyTest
    {
        // Mirrors MeasureSleep: how long Sleep(1) really takes at the current timer resolution.
        public static string SleepPrecision(int samples)
        {
            var d = new List<double>(samples);
            var sw = new Stopwatch();
            System.Threading.Thread.Sleep(1);
            for (int i = 0; i < samples; i++)
            {
                sw.Restart(); System.Threading.Thread.Sleep(1); sw.Stop();
                d.Add(sw.Elapsed.TotalMilliseconds);
            }
            d.Sort();
            double avg = d.Average(), p99 = d[(int)(d.Count * 0.99) - 1], mx = d[d.Count - 1], mn = d[0];
            double dev = Math.Sqrt(d.Sum(x => (x - avg) * (x - avg)) / d.Count);
            string verdictKey;
            string verdict = avg < 1.15 ? "excellent (≈0.5 ms timer active)" : avg < 1.6 ? "good (0.5–1 ms timer)" : avg < 2.2 ? "fair (1 ms timer)" : "coarse (default 15.6 ms timer or no request active)";
            verdictKey = verdict.Split(' ')[0];
            try
            {
                using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(@"Software\ArchTweaker"))
                    k.SetValue("LastSleepTest", string.Format("avg {0:0.000} ms, p99 {1:0.000} ms ({2}) on {3:yyyy-MM-dd HH:mm}", avg, p99, verdictKey, DateTime.Now));
            }
            catch { }
            return string.Format("Sleep(1) × {0}:  avg {1:0.000} ms · min {2:0.000} · p99 {3:0.000} · max {4:0.000} · stdev {5:0.000}  →  {6}\r\n{7}",
                samples, avg, mn, p99, mx, dev, verdict, TimerRes.Query());
        }
    }
}
