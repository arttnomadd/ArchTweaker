// ArchTweaker: Latency dashboard page and its per-core bars.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ===================== LATENCY =====================
    public class LatencyPage : Panel, IPage
    {
        readonly CoreBars bars = new CoreBars();
        readonly ListView drivers = new ListView { Dock = DockStyle.Fill };
        readonly Label timer, summary, traceNote;
        readonly ComboBox secs = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, AccessibleName = "Scan length" };   // 3.8
        List<DriverStat> last;
        readonly Timer tick = new Timer { Interval = 1000 };
        CoreCounters counters;
        Button fix;
        readonly RunCompare compare = new RunCompare();
        public LatencyPage()
        {
            BackColor = Theme.Bg;
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Theme.Bg, Padding = new Padding(0, 0, 0, Theme.S(6)) };
            timer = new Label { AutoSize = true, Font = Theme.FB, ForeColor = Theme.Text, Margin = new Padding(0, 0, Theme.S(24), 0), UseMnemonic = false };
            summary = new Label { AutoSize = true, Font = Theme.F, ForeColor = Theme.Muted, UseMnemonic = false };
            fix = Theme.Btn("Fix counters", delegate { AppShell.Current.Busy("Rebuilding counters…", Tools.RebuildPerfCounters, delegate { if (!IsDisposed && Visible) Start(); }); });   // 3.17.1
            fix.Visible = false; fix.Margin = new Padding(Theme.S(12), 0, 0, 0);
            top.Controls.Add(timer); top.Controls.Add(summary); top.Controls.Add(fix);
            bars.Dock = DockStyle.Top; bars.Height = Theme.S(210);

            var bar = Theme.Bar();
            secs.Items.AddRange(new object[] { "10 s", "30 s", "60 s" }); secs.SelectedIndex = 0; secs.Margin = new Padding(0, Theme.S(5), Theme.S(8), 0);
            bar.Controls.Add(secs);
            bar.Controls.Add(Theme.Lead("Find slow drivers", delegate { Trace(); }));
            bar.Controls.Add(Theme.Btn("Copy report", delegate { CopyReport(); }));
            bar.Controls.Add(Theme.Btn("Open Measure page", delegate { AppShell.Current.ShowPage("Measure"); }));
            // 3.14: off by default; 30 s after each apply that is live at once
            var auto = new SwitchBox { Text = "Scan after each apply", Checked = AfterApplyScan.Enabled, ForeColor = Theme.Text, Margin = new Padding(12, 2, 0, 0), AccessibleDescription = "Records driver activity for 30 seconds after each apply that needs no restart." };
            auto.CheckedChanged += delegate { AfterApplyScan.Enabled = auto.Checked; };
            bar.Controls.Add(auto);
            traceNote = new Label { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.FS, Margin = new Padding(Theme.S(12), Theme.S(8), 0, 0), UseMnemonic = false };
            bar.Controls.Add(traceNote);

            Theme.DarkList(drivers);
            drivers.Columns.Add("Driver", 240); drivers.Columns.Add("Worst DPC", 100); drivers.Columns.Add("Worst ISR", 100);
            drivers.Columns.Add("DPCs", 80); drivers.Columns.Add("ISRs", 80); drivers.Columns.Add("Time in DPC/ISR", 130); drivers.Columns.Add("Verdict", 120);
            drivers.Columns.Add("≥ 500 µs", 80); drivers.Columns.Add("≥ 1 ms", 70); drivers.Columns.Add("What it is", 260);   // 3.8
            drivers.DoubleClick += delegate { ShowAdvice(); };

            var head = new Label { Text = "Drivers", Font = Theme.FB, ForeColor = Theme.Text, Dock = DockStyle.Top, Height = Theme.S(28), TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false };
            Controls.Add(drivers); Controls.Add(head); Controls.Add(compare.Trend); Controls.Add(compare); Controls.Add(bar);   // 3.17: trend under the cards   // 3.13: compare sits under the button bar
            Controls.Add(Theme.Note("Per-core load from interrupts (ISR) and deferred procedure calls (DPC), updated every second. Shaded columns are fast cores (P-cores). \"Find slow drivers\" records 10, 30 or 60 seconds of all DPCs and ISRs and shows their drivers. Over 500 µs can cause audio crackle and frame hitches. Over 1000 µs is bad."));
            Controls.Add(bars); Controls.Add(top);
            tick.Tick += delegate { Sample(); };
            VisibleChanged += delegate { if (Visible) Start(); else tick.Stop(); };
            Disposed += delegate { tick.Stop(); tick.Dispose(); if (counters != null) counters.Dispose(); };
            Skin.SetEmpty(drivers, "Press \"Find slow drivers\" to record driver activity. Double-click a driver to see what it is and which tweaks help.");
        }
        public void OnShow() { compare.Reload(); Start(); }
        public void Filter(string q) { }
        void Start()
        {
            if (counters != null && !counters.Ok) { counters.Dispose(); counters = null; }   // 3.17.1: retry, e.g. after a rebuild
            if (counters == null) counters = new CoreCounters();
            fix.Visible = !counters.Ok;
            bars.Empty = counters.Ok ? null : "No counter data";
            if (!counters.Ok) { summary.Text = counters.Error ?? "Processor counters are not available."; return; }
            tick.Start(); Sample();
        }
        void Sample()
        {
            double ms = -1; try { ms = TimerRes.CurrentMs(); } catch { }
            timer.Text = ms > 0 ? "Timer resolution: " + ms.ToString("0.000") + " ms" : "";
            var s = counters.Read();
            if (s.Count == 0) return;
            bars.Set(s);
            var worst = s.OrderByDescending(x => x.DpcPct + x.IsrPct).First();
            summary.Text = "Busiest core: CPU " + worst.Index + " — DPC " + worst.DpcPct.ToString("0.0") + " %, ISR " + worst.IsrPct.ToString("0.0") + " %   ·   " + s.Sum(x => x.IntsPerSec).ToString("0") + " interrupts/s";
        }
        int Seconds { get { return secs.SelectedIndex == 2 ? 60 : secs.SelectedIndex == 1 ? 30 : 10; } }
        void Trace()
        {
            List<DriverStat> res = null; string err = null; int n = Seconds;
            AppShell.Current.Busy(L.F("Recording driver activity for {0} seconds…", n), delegate { res = DriverTrace.Run(n, out err); }, delegate
            {
                if (res == null)
                {
                    // 3.8: a leftover kernel trace (crashed scan) can be stopped from here
                    if (DriverTrace.Busy && Dlg.Confirm("The kernel trace is already in use", "LatencyMon, xperf, Process Monitor or an earlier scan is using the Windows kernel trace. If none of those are open, it is a leftover: stop it and scan again?", "Stop it and scan"))
                    { DriverTrace.StopKernelLogger(); Trace(); return; }
                    if (!DriverTrace.Busy) Dlg.Error("Couldn't record driver activity", err ?? "Unknown error.");
                    return;
                }
                last = res;
                LatencyRuns.Save(LatencyRuns.From(res, n)); compare.Reload();   // 3.13: kept for before/after
                drivers.BeginUpdate(); drivers.Items.Clear();
                foreach (var d in res.Where(x => x.Dpcs + x.Isrs > 0).Take(40))
                {
                    int lvl = DriverTrace.Level(d.MaxUs);
                    var adv = DriverAdvice.For(d.Driver);
                    var it = new ListViewItem(new[] { d.Driver, d.Dpcs > 0 ? d.DpcMaxUs.ToString("0") + " µs" : "—", d.Isrs > 0 ? d.IsrMaxUs.ToString("0") + " µs" : "—",
                        d.Dpcs.ToString(), d.Isrs.ToString(), ((d.DpcTotalUs + d.IsrTotalUs) / 1000).ToString("0.0") + " ms", L.T(lvl == 2 ? "Bad" : lvl == 1 ? "High" : "OK"),
                        d.Over500.ToString(), d.Over1000.ToString(), adv != null ? L.T(adv.What) : "" }) { Tag = d };
                    it.ForeColor = lvl == 2 ? Theme.Bad : lvl == 1 ? Theme.Warn : Theme.Text;
                    drivers.Items.Add(it);
                }
                drivers.EndUpdate(); Skin.Loaded(drivers, "ArchTweaker recorded no driver activity.");
                var top = res.FirstOrDefault();
                int unknown = res.Where(x => x.Driver == "unknown").Sum(x => x.Dpcs + x.Isrs), all = res.Sum(x => x.Dpcs + x.Isrs);
                traceNote.Text = top == null ? "" : L.F("Worst: {0} ({1} µs). Double-click a driver for what to try.", top.Driver, top.MaxUs.ToString("0"))
                    + (all > 0 && unknown * 2 > all ? "  " + L.T("ArchTweaker couldn't match most addresses to a driver.") : "");
            });
        }

        // 3.8: what the selected driver is and which tweaks address it
        void ShowAdvice()
        {
            if (drivers.SelectedItems.Count == 0) return;
            var d = drivers.SelectedItems[0].Tag as DriverStat; if (d == null) return;
            var adv = DriverAdvice.For(d.Driver);
            string head = L.F("Worst {0} µs · {1} at least 500 µs · {2} at least 1 ms", d.MaxUs.ToString("0"), d.Over500, d.Over1000);
            if (adv == null) { Dlg.Info(d.Driver, head + "\r\n\r\n" + L.T("No advice for this driver. Update it from the device maker's site, or disable the device in Device Manager if you don't use it.")); return; }
            var tw = Catalog.All.Where(t => adv.Ids.Contains(t.Id) && t.Applicable).ToList();
            string body = head + "\r\n\r\n" + L.T(adv.What) + "\r\n" + L.T(adv.Try) + (tw.Count > 0 ? "\r\n\r\n" + L.T("Tweaks that address it:") + "\r\n" + string.Join("\r\n", tw.Select(t => "• " + L.T(t.Title) + (StateCache.Get(t) == true ? "  (" + L.T("on") + ")" : "")).ToArray()) : "");
            var off = tw.Where(t => StateCache.Get(t) != true).ToList();
            if (off.Count > 0) { if (Dlg.Confirm(d.Driver, body, L.F("Select {0} tweaks", off.Count))) AppShell.Current.SelectIds(off.Select(t => t.Id)); }
            else Dlg.Info(d.Driver, body);
        }

        // 3.8: the scan as text, for a forum post or a support request
        void CopyReport()
        {
            if (last == null) { AppShell.Current.Notify(L.T("Run a scan first."), Toast.Kind.Warn); return; }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ArchTweaker " + Program.Version + " · slow-driver scan · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " · " + OSInfo.Name);
            sb.AppendLine(string.Format("{0,-28} {1,10} {2,10} {3,8} {4,8} {5,8} {6,7}", "Driver", "Worst DPC", "Worst ISR", "DPCs", "ISRs", ">=500us", ">=1ms"));
            foreach (var d in last.Where(x => x.Dpcs + x.Isrs > 0).Take(40))
                sb.AppendLine(string.Format("{0,-28} {1,10:0} {2,10:0} {3,8} {4,8} {5,8} {6,7}", d.Driver, d.DpcMaxUs, d.IsrMaxUs, d.Dpcs, d.Isrs, d.Over500, d.Over1000));
            try { Clipboard.SetText(sb.ToString()); AppShell.Current.Notify(L.T("Report copied."), Toast.Kind.Ok); } catch { }
        }
    }

    // Per-core vertical bars: DPC (accent) stacked under ISR (warn colour).
    public class CoreBars : Control
    {
        List<CoreSample> s = new List<CoreSample>();
        string empty;
        public string Empty { get { return empty; } set { empty = value; Invalidate(); } }   // 3.17.1: shown instead of "Reading counters…"
        public CoreBars()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AccessibleRole = AccessibleRole.Chart; AccessibleName = "Per-core DPC and interrupt load";
        }
        public void Set(List<CoreSample> v) { s = v; Invalidate(); AccessibleDescription = string.Join(", ", v.Select(x => "CPU " + x.Index + ": " + (x.DpcPct + x.IsrPct).ToString("0.0") + "%").ToArray()); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Bg);
            using (var b = new SolidBrush(Theme.Panel)) using (var p = Gfx.Round(new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(Theme.Radius))) g.FillPath(b, p);
            if (s.Count == 0) { TextRenderer.DrawText(g, L.T(empty ?? "Reading counters…"), Theme.F, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); return; }
            int pad = Theme.S(14), labelH = Theme.S(18), top = Theme.S(30);
            var plot = new Rectangle(pad + Theme.S(28), top, Width - pad * 2 - Theme.S(28), Height - top - labelH - Theme.S(10));
            double max = Math.Max(5, Math.Ceiling(s.Max(x => x.DpcPct + x.IsrPct) / 5) * 5);
            using (var grid = new Pen(Theme.Grid))
                for (int i = 0; i <= 4; i++)
                {
                    int y = plot.Bottom - (int)(plot.Height * i / 4.0);
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    TextRenderer.DrawText(g, (max * i / 4).ToString("0") + "%", Theme.FS, new Rectangle(pad - 4, y - 8, Theme.S(32), 16), Theme.Muted, TextFormatFlags.Right);
                }
            float slot = plot.Width / (float)s.Count, w = Math.Max(2, Math.Min(Theme.S(26), slot * 0.62f));
            using (var dpc = new SolidBrush(Theme.Accent)) using (var isr = new SolidBrush(Theme.Warn)) using (var fast = new SolidBrush(Gfx.Mix(Theme.Panel, Theme.Text, 0.06)))
                for (int i = 0; i < s.Count; i++)
                {
                    float x = plot.Left + slot * i + (slot - w) / 2;
                    if (s[i].Fast && HwProbe.Cpu.Hybrid) g.FillRectangle(fast, plot.Left + slot * i + 1, plot.Top, slot - 2, plot.Height);
                    float hd = (float)(plot.Height * s[i].DpcPct / max), hi = (float)(plot.Height * s[i].IsrPct / max);
                    g.FillRectangle(dpc, x, plot.Bottom - hd, w, hd);
                    g.FillRectangle(isr, x, plot.Bottom - hd - hi, w, hi);
                    if (slot >= Theme.S(14) || i % 2 == 0)
                        TextRenderer.DrawText(g, s[i].Index.ToString(), Theme.FS, new Rectangle((int)(plot.Left + slot * i), plot.Bottom + 2, (int)slot, labelH), Theme.Muted, TextFormatFlags.HorizontalCenter);
                }
            int lx = plot.Left;
            using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, lx, Theme.S(10), Theme.S(10), Theme.S(10));
            string l1 = L.T("DPC time");
            TextRenderer.DrawText(g, l1, Theme.FS, new Point(lx + Theme.S(14), Theme.S(7)), Theme.Text);
            lx += Theme.S(24) + TextRenderer.MeasureText(l1, Theme.FS).Width;
            using (var b = new SolidBrush(Theme.Warn)) g.FillRectangle(b, lx, Theme.S(10), Theme.S(10), Theme.S(10));
            TextRenderer.DrawText(g, L.T("Interrupt time"), Theme.FS, new Point(lx + Theme.S(14), Theme.S(7)), Theme.Text);
        }
    }
}
