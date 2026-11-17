// ArchTweaker: Timer wake-up delay: how late a thread wakes from a 1 ms high-resolution timer.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static class WakeLatency
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateWaitableTimerExW(IntPtr attr, string name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetWaitableTimer(IntPtr h, ref long due, int period, IntPtr cb, IntPtr arg, bool resume);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x2, TIMER_ALL_ACCESS = 0x1F0003;

        public static double P99(List<double> lateUs)
        {
            if (lateUs == null || lateUs.Count == 0) return -1;
            var s = lateUs.OrderBy(x => x).ToList();
            return PresentMon.Quantile(s, 0.99);
        }

        // Late wake-ups in µs over `secs` seconds; null (with the reason) when it cannot measure, for example before
        // Windows 10 1803, which has no high-resolution timer. 3.27.1: a wait that did not end by the timer is skipped.
        public static List<double> Sample(int secs) { string why; return Sample(secs, out why); }
        public static List<double> Sample(int secs, out string why)
        {
            why = null;
            IntPtr h = IntPtr.Zero;
            try { h = CreateWaitableTimerExW(IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS); }
            catch (Exception ex) { why = ex.Message; return null; }
            if (h == IntPtr.Zero) { why = "no high-resolution timer (Windows error " + Marshal.GetLastWin32Error() + ")"; return null; }
            var o = new List<double>(secs * 900);
            var prio = Thread.CurrentThread.Priority;
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
                var all = Stopwatch.StartNew();
                double f = Stopwatch.Frequency;
                while (all.ElapsedMilliseconds < secs * 1000L)
                {
                    long due = -10000;   // 1 ms, relative, in 100 ns units
                    long t0 = Stopwatch.GetTimestamp();
                    if (!SetWaitableTimer(h, ref due, 0, IntPtr.Zero, IntPtr.Zero, false)) { why = "SetWaitableTimer failed (Windows error " + Marshal.GetLastWin32Error() + ")"; return null; }
                    if (WaitForSingleObject(h, 1000) != 0) continue;   // not WAIT_OBJECT_0: no valid sample
                    double us = (Stopwatch.GetTimestamp() - t0) * 1e6 / f - 1000.0;
                    o.Add(Math.Max(0, us));
                }
            }
            catch (Exception ex) { why = ex.Message; return null; }
            finally { Thread.CurrentThread.Priority = prio; CloseHandle(h); }
            return o;
        }
    }
}
