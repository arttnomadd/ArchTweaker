// ArchTweaker: Cleanup page.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ================= CLEANUP =================
    public class CleanupPage : Panel, IPage
    {
        List<CleanTarget> targets = CleanupModule.Targets();
        Dictionary<CleanTarget, CheckRow> checks = new Dictionary<CleanTarget, CheckRow>();
        Label total;
        public CleanupPage()
        {
            BackColor = Theme.Bg;
            var list = new CheckList();
            foreach (var t in targets)
            {
                var r = new CheckRow(t.Title, t.Hint, false, t.Default) { Margin = new Padding(0, 0, 0, Theme.S(6)) };
                r.CheckedChanged += delegate { UpdateTotal(); };
                checks[t] = r; list.Controls.Add(r);
            }
            var bar = Theme.Bar();
            bar.Controls.Add(Theme.Lead("Clean selected", delegate { Clean(); }));
            bar.Controls.Add(Theme.Btn("Measure sizes", delegate { Scan(); }));
            bar.Controls.Add(Theme.Btn("Preview…", delegate
            {
                var sel = targets.Where(t => checks[t].Checked && t.Dirs != null).ToList();
                if (sel.Count == 0) { MainForm.Instance.Notify("Tick at least one item to preview.", Toast.Kind.Warn); return; }
                CleanPreview.Open(MainForm.Instance, sel); Scan();
            }));
            bar.Controls.Add(Theme.Btn("Select all", delegate { foreach (var r in checks.Values) r.Checked = true; }));
            bar.Controls.Add(Theme.Btn("Select none", delegate { foreach (var r in checks.Values) r.Checked = false; }));
            bar.Controls.Add(Theme.Btn("Component store cleanup (DISM)", delegate { Tools.ComponentCleanup(); }));
            total = new Label { AutoSize = true, ForeColor = Theme.Text, Font = Theme.FB, Margin = new Padding(12, 8, 0, 0), UseMnemonic = false };
            bar.Controls.Add(total);
            Controls.Add(list); Controls.Add(bar);
        }
        bool scanned;
        public void OnShow() { if (!scanned) Scan(); }
        public void Filter(string q) { }
        void UpdateTotal()
        {
            if (!scanned) return;
            int n = targets.Count(t => checks[t].Checked);
            total.Text = "Selected: " + CleanupModule.Fmt(targets.Where(t => checks[t].Checked).Sum(t => Math.Max(0, t.Size))) + " in " + n + (n == 1 ? " item" : " items");
        }
        void Scan()
        {
            scanned = true;
            MainForm.Instance.Busy("Measuring…", () => { foreach (var t in targets) CleanupModule.Measure(t); }, () =>
            {
                foreach (var t in targets) checks[t].SizeText = t.Title == "Event logs" ? "" : CleanupModule.Fmt(t.Size);
                UpdateTotal();
            });
        }
        void Clean()
        {
            var sel = targets.Where(t => checks[t].Checked).ToList();
            if (sel.Count == 0) { MainForm.Instance.Notify("Nothing selected. Tick at least one item to clean.", Toast.Kind.Warn); return; }
            long freed = 0; int skipped = 0;
            MainForm.Instance.Busy("Cleaning…", () =>
            {
                foreach (var t in sel)
                {
                    try { int sk; freed += CleanupModule.Clean(t, out sk); skipped += sk; }
                    catch (Exception ex) { Log.W("Clean failed for {0}: {1}", t.Title, ex.Message); }
                }
            }, () =>
            {
                string fr = CleanupModule.Fmt(freed);
                string msg = skipped == 0 ? L.F("Freed {0}.", fr) : skipped == 1 ? L.F("Freed {0}. 1 file is in use, so Cleanup skipped it. Close open apps or restart, then clean again.", fr) : L.F("Freed {0}. {1} files are in use, so Cleanup skipped them. Close open apps or restart, then clean again.", fr, skipped);
                MainForm.Instance.Notify(msg, skipped > 0 ? Toast.Kind.Warn : Toast.Kind.Ok);
                CleanSettings.MarkCleaned(freed);
                Scan();
            });
        }
    }
}
