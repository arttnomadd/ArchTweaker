// ArchTweaker: Drift guard: applied tweaks that something turned back.
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
    public static class DriftGuard
    {
        const string Key = @"Software\ArchTweaker";
        public static string KeyOf(IEnumerable<string> ids) { return string.Join(",", ids.OrderBy(x => x, StringComparer.Ordinal).ToArray()); }

        // Applied tweaks that went back, minus a set the user already dismissed.
        public static List<DriftItem> Check()
        {
            var ids = AppliedSet.Read() ?? new List<string>();
            if (ids.Count == 0) return new List<DriftItem>();
            var d = Drift.Against(ids).Where(x => x.DesiredApplied && !x.Actual).ToList();
            if (d.Count == 0) return d;
            string dismissed = Reg.Get(Hive.HKCU, Key, "DriftDismissed") as string;
            return dismissed == KeyOf(d.Select(x => x.Tweak.Id)) ? new List<DriftItem>() : d;
        }
        public static void Dismiss(IEnumerable<DriftItem> d)
        {
            try { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(Key)) k.SetValue("DriftDismissed", KeyOf(d.Select(x => x.Tweak.Id))); } catch { }
        }
    }

    // 3.26: drift guard while the app runs. Every 30 minutes it looks at three Windows logs that change when a driver
    // or an update installs. When one changed, or every 3 hours anyway, it checks the applied tweaks again.
    // It never checks during an apply or a tuner run: those change tweaks on purpose.
    public static class DriftWatch
    {
        static System.Threading.Timer timer;
        static DateTime lastSignal, lastFull;
        static string lastKey = "";
        static volatile bool forceNext;
        static int failures;
        static Func<bool> busy; static Func<List<DriftItem>, bool> show;
        public static readonly TimeSpan Every = TimeSpan.FromMinutes(30), Full = TimeSpan.FromHours(3);

        public static IEnumerable<string> SignalFiles()
        {
            string w = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            yield return System.IO.Path.Combine(w, @"INF\setupapi.dev.log");                 // driver installs
            yield return System.IO.Path.Combine(w, @"SoftwareDistribution\ReportingEvents.log"); // Windows Update
            yield return System.IO.Path.Combine(w, @"Logs\CBS\CBS.log");                     // servicing
        }
        static bool loggedNoSignal;
        static DateTime Newest()
        {
            var t = DateTime.MinValue; int read = 0;
            foreach (var f in SignalFiles()) try { if (System.IO.File.Exists(f)) { var m = System.IO.File.GetLastWriteTimeUtc(f); read++; if (m > t) t = m; } } catch { }
            if (read == 0 && !loggedNoSignal) { loggedNoSignal = true; Log.W("Drift guard: no driver or update log is readable; checking every {0} h only.", Full.TotalHours); }
            return t;
        }
        // True when a check is due: a signal file changed, or the last full check is older than Full.
        public static bool Due(DateTime signalBefore, DateTime signalNow, DateTime lastCheck, DateTime now)
        {
            return signalNow > signalBefore || now - lastCheck >= Full;
        }
        // The set the person already saw (shown at start, or dismissed) is not shown again.
        public static void Seen(IEnumerable<DriftItem> d) { lastKey = d == null ? "" : DriftGuard.KeyOf(d.Select(x => x.Tweak.Id)); }

        // 3.27.1: the delegates are set again on every call, so after a window rebuild they point at the new window.
        // 'onDrift' returns false when it could not show the notice; the same set is then tried again next time.
        public static void Start(Func<bool> isBusy, Func<List<DriftItem>, bool> onDrift)
        {
            busy = isBusy; show = onDrift;
            if (timer != null) return;
            lastSignal = Newest(); lastFull = DateTime.UtcNow;
            timer = new System.Threading.Timer(delegate { Tick(); }, null, Every, Every);
            try { Microsoft.Win32.SystemEvents.PowerModeChanged += (s, e) => { if (e.Mode == Microsoft.Win32.PowerModes.Resume) { forceNext = true; try { timer.Change(TimeSpan.FromMinutes(2), Every); } catch { } } }; } catch { }
        }
        static int ticking;
        static void Tick()
        {
            if (Interlocked.Exchange(ref ticking, 1) == 1) return;
            try
            {
                if (busy != null && busy()) return;
                var sig = Newest(); var now = DateTime.UtcNow;
                bool force = forceNext; forceNext = false;
                if (!force && !Due(lastSignal, sig, lastFull, now)) return;
                lastSignal = sig; lastFull = now;
                var d = DriftGuard.Check();
                failures = 0;
                string key = DriftGuard.KeyOf(d.Select(x => x.Tweak.Id));
                if (d.Count == 0) { lastKey = ""; return; }
                if (key == lastKey) return;
                Log.W("Drift guard (while running): {0} tweak(s) went back.", d.Count);
                if (show != null && show(d)) lastKey = key;
            }
            catch (Exception ex)
            {
                Log.W("Drift guard: {0}", ex.Message);
                if (++failures == 3) Log.Say("Drift guard could not check the applied tweaks three times in a row: {0}", ex.Message);
            }
            finally { Interlocked.Exchange(ref ticking, 0); }
        }
    }
}
