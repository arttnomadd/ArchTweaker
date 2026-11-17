// ArchTweaker: Health readings for the health strip.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ================= HEALTH SCORE (Home) =================
    public class HealthInfo
    {
        public int Score, SpeedApplied, SpeedTotal, Drifted; public bool RestartPending; public DateTime LastClean; public double DpcIsrPct = -1;
    }

    public static class Health
    {
        public static HealthInfo Read(bool restartPending)
        {
            var h = new HealthInfo { RestartPending = restartPending, LastClean = CleanSettings.LastClean };
            var speed = Catalog.All.Where(t => t.Applicable && Mode.Allows(t) && t.Impact > 0 && !t.Dangerous && !t.Experimental).ToList();   // 3.4: the score never asks for Danger zone tweaks
            h.SpeedTotal = speed.Count;
            foreach (var t in speed) if (StateCache.Applied(t)) h.SpeedApplied++;   // 3.6.4: read once and cached for every other reader
            try { h.Drifted = Drift.Against(AppliedSet.Read()).Count; } catch { }
            double d, i; CoreCounters.Average(1000, out d, out i); if (d >= 0) h.DpcIsrPct = d + i;
            h.Score = Score(h);
            return h;
        }
        // 50 tuning + 20 nothing reset + 10 no pending restart + 10 cleaned in the last 2 weeks + 10 low DPC/ISR load
        public static int Score(HealthInfo h)
        {
            double s = h.SpeedTotal > 0 ? 50.0 * h.SpeedApplied / h.SpeedTotal : 50;
            s += Math.Max(0, 20 - 5 * h.Drifted);
            if (!h.RestartPending) s += 10;
            if ((DateTime.Now - h.LastClean).TotalDays <= 14) s += 10;
            if (h.DpcIsrPct >= 0 && h.DpcIsrPct < 2) s += 10; else if (h.DpcIsrPct < 0) s += 5;
            return (int)Math.Round(Math.Max(0, Math.Min(100, s)));
        }
    }
}
