// ArchTweaker: Quick scan after apply.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ArchTweaker
{
    // ================= LATENCY SCAN AFTER APPLY =================
    // Off by default. After an apply that needs no restart, records driver activity for 30 s and saves the run,
    // so Home, the Latency page and the apply history show what the apply did.
    public static class AfterApplyScan
    {
        public const int Secs = 30;
        const string Key = @"Software\ArchTweaker";
        public static bool Enabled
        {
            get { object o = Reg.Get(Hive.HKCU, Key, "LatencyAfterApply"); return o is int && (int)o == 1; }
            set { try { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(Key)) k.SetValue("LatencyAfterApply", value ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord); } catch { } }
        }
        // the sentence for the toast and the log; prev = the run before this one, or null
        public static string Summary(LatencyRun now, LatencyRun prev)
        {
            string s = L.F("Latency after apply: worst DPC/ISR {0} µs", now.WorstUs.ToString("0"));
            if (prev != null && prev.WorstUs > 0)
            {
                double d = CompareCards.Delta(prev.WorstUs, now.WorstUs);
                s += Math.Abs(d) < 10 ? " (" + L.T("about the same as before") + ")" : " (" + (d < 0 ? "▼ " : "▲ ") + Math.Abs(d).ToString("0") + " % " + L.T("since the scan before") + ")";
            }
            return s;
        }
        public static void Run(MainForm m)
        {
            List<DriverStat> res = null; string err = null;
            m.Busy(L.F("Latency scan after apply ({0} seconds)…", Secs), delegate { res = DriverTrace.Run(Secs, out err); }, delegate
            {
                if (res == null) { Log.Say("Latency scan after apply did not run: {0}", err ?? "the kernel trace is in use"); return; }
                var prev = LatencyRuns.All().LastOrDefault();
                var run = LatencyRuns.From(res, Secs);
                LatencyRuns.Save(run);
                History.NoteLast(run.WorstUs.ToString("0", CultureInfo.InvariantCulture));
                m.Notify(Summary(run, prev), Toast.Kind.Ok);
                m.RefreshHomeTiles();
            });
        }
    }
}
