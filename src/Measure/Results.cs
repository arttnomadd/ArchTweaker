// ArchTweaker: Results page: every saved run of every test and tuner in one list, newest first, and one button that
// puts back everything the tuners changed. The tools keep their own files (formats unchanged); this reads them all.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    public class ResultRow
    {
        public DateTime When; public string Tool = "", Summary = "", Report;
    }

    public static class Results
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Report file name prefix → tool name.
        public static readonly Dictionary<string, string> ReportTools = new Dictionary<string, string>
        {
            { "tuning_", "Tuning assistant" }, { "irq_", "Interrupt tuner" }, { "power_", "Power tuner" }, { "tuneall_", "Tune everything" },
        };

        // "tuning_20261114-203015.html" → its tool and time; false for other files (benchmark reports have their own rows).
        public static bool ParseReportName(string file, out string tool, out DateTime when)
        {
            tool = null; when = DateTime.MinValue;
            string n = file ?? ""; n = n.Substring(Math.Max(n.LastIndexOf('\\'), n.LastIndexOf('/')) + 1); n = Path.GetFileNameWithoutExtension(n);
            foreach (var kv in ReportTools)
                if (n.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)
                    && DateTime.TryParseExact(n.Substring(kv.Key.Length), "yyyyMMdd-HHmmss", Inv, DateTimeStyles.None, out when))
                { tool = kv.Value; return true; }
            return false;
        }

        public static List<ResultRow> All()
        {
            var o = new List<ResultRow>(); Unread = new List<string>();
            Try("Latency", () => { foreach (var r in LatencyRuns.All()) o.Add(new ResultRow { When = r.When, Tool = "Latency", Summary = L.F("Worst DPC/ISR {0:0} µs · {1:0.0} spikes per minute", r.WorstUs, r.SpikesPerMin) }); });
            Try("Benchmark", () => { foreach (var r in BenchRuns.All()) o.Add(new ResultRow { When = r.When, Tool = "Benchmark", Report = Bench.ReportIn(r.Report),
                Summary = r.HasFrames ? L.F("{0}: {1:0} FPS average · {2:0} FPS 1 % low", r.Game, r.AvgFps, r.P1Fps) : L.F("Sleep(1) {0:0.00} ms (99th percentile) · worst DPC/ISR {1:0} µs", r.SleepP99, r.WorstUs) }); });
            Try("Measure", () => { foreach (var r in MeasureRuns.All()) o.Add(new ResultRow { When = r.M.When, Tool = "Measure", Summary = L.F("Sleep(1) {0:0.00} ms (99th percentile)", r.M.Sleep1P99) + (r.Baseline ? " · " + L.T("baseline") : "") }); });
            Try("Mouse polling", () => { foreach (var r in PollRuns.All()) o.Add(new ResultRow { When = r.When, Tool = "Mouse polling", Summary = L.F("{0}: {1:0} Hz · jitter {2:0} µs", r.Device, r.RateHz, r.JitterUs) }); });
            Try("Report", () =>
            {
                if (!Directory.Exists(Bench.ReportDir)) return;
                foreach (var f in Directory.GetFiles(Bench.ReportDir, "*.html"))
                {
                    string tool; DateTime when;
                    if (ParseReportName(f, out tool, out when)) o.Add(new ResultRow { When = when, Tool = tool, Report = f, Summary = L.T("Report") });
                }
            });
            return o.OrderByDescending(r => r.When).ToList();
        }
        // 3.27.1: the tools whose runs could not be read, for a note on the page
        [ThreadStatic] public static List<string> Unread;
        static void Try(string tool, Action a) { try { a(); } catch (Exception ex) { Log.W("Results: {0}: {1}", tool, ex.Message); if (Unread != null) Unread.Add(L.T(tool)); } }

        public static bool AnyTunerBackup { get { return IrqTuner.HasBackup || PowerTuner.HasBackup; } }

        // Puts back the interrupt settings and power values from before the first tuner run.
        public static string PutBackTunerChanges()
        {
            string e1 = null, e2 = null;
            int irq = IrqTuner.HasBackup ? IrqTuner.RestoreAll(out e1) : 0;
            int pow = PowerTuner.HasBackup ? PowerTuner.RestoreAll(out e2) : 0;
            string msg = L.F("Put back {0} interrupt setting(s) and {1} power value(s).", irq, pow);
            var errs = new[] { e1, e2 }.Where(x => x != null).ToArray();
            return errs.Length == 0 ? msg : msg + " " + L.F("Not put back: {0}. Click the button again to retry.", string.Join("; ", errs));
        }
    }

    public class ResultsPage : Panel, IPage
    {
        readonly ListView lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
        readonly Button openBtn, backBtn;
        string filter = "";
        List<ResultRow> rows = new List<ResultRow>();
        public ResultsPage()
        {
            BackColor = Theme.Bg;
            Theme.DarkList(lv);
            lv.Columns.Add("When", 150); lv.Columns.Add("Tool", 170); lv.Columns.Add("Result", 560);
            var bar = Theme.Bar();
            openBtn = Theme.Btn("Open report", delegate { Open(); }); openBtn.Enabled = false; bar.Controls.Add(openBtn);
            bar.Controls.Add(Theme.Btn("Refresh", delegate { Reload(); }));
            backBtn = Theme.Btn("Put back everything the tuners changed", delegate { Back(); }); bar.Controls.Add(backBtn);
            lv.SelectedIndexChanged += delegate { var r = Sel(); openBtn.Enabled = r != null && r.Report != null && File.Exists(r.Report); };
            lv.DoubleClick += delegate { Open(); };
            Controls.Add(lv);
            Controls.Add(Theme.Note("Every saved run of Latency, Benchmark, Measure, Mouse polling and the three tuners, newest first. Each tool keeps its own runs."));
            Controls.Add(bar);
            Skin.SetEmpty(lv, "Loading results…");
        }
        public void OnShow() { Reload(); }
        public void Filter(string q) { filter = q ?? ""; Fill(); }
        ResultRow Sel() { return lv.SelectedItems.Count == 1 ? lv.SelectedItems[0].Tag as ResultRow : null; }
        void Open() { var r = Sel(); string f = r == null ? null : Bench.ReportIn(r.Report); if (f != null && File.Exists(f)) Shell.OpenVisible("explorer.exe", "\"" + f + "\""); }

        void Reload()
        {
            backBtn.Enabled = Results.AnyTunerBackup;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                var l = Results.All(); var bad = Results.Unread ?? new List<string>();
                if (!IsHandleCreated || IsDisposed) return;
                try { BeginInvoke((Action)(() => { rows = l; Fill(); if (bad.Count > 0) Log.Say("Results: could not read the saved runs of {0}. The log says why.", string.Join(", ", bad.ToArray())); })); } catch (InvalidOperationException) { }
            });
        }
        void Fill()
        {
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var r in rows)
            {
                string tool = L.T(r.Tool);
                if (filter.Length > 0 && tool.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && r.Summary.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                lv.Items.Add(new ListViewItem(new[] { r.When.ToString("g", CultureInfo.CurrentCulture), tool, r.Summary }) { Tag = r });
            }
            lv.EndUpdate();
            Skin.Loaded(lv, "No saved runs yet. Run Latency, Benchmark, Measure, Mouse polling or a tuner.");
        }
        void Back()
        {
            if (!Dlg.Confirm("Put back everything the tuners changed?", "The Interrupt tuner and the Power tuner keep the settings from before their first run. ArchTweaker writes those back. Tweaks that the Tuning assistant kept are normal applies: Undo last or Change history takes them back.", "Put back", "Cancel")) return;
            string msg = null;
            MainForm.Instance.Busy("Putting back the tuner changes…", () => { msg = Results.PutBackTunerChanges(); }, () => { Log.Say("{0}", msg); Reload(); });
        }
    }
}
