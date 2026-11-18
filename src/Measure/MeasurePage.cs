// ArchTweaker: Measure page: before/after numbers measured on this PC.
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    public class MeasurePage : Panel, IPage
    {
        TextBox outBox, proc, secs; Label emptyHint;
        MetricSet before;
        readonly MeasureCompare compare = new MeasureCompare();
        public MeasurePage()
        {
            BackColor = Theme.Bg;
            outBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Mono };

            var bar = Theme.Bar();
            bar.Controls.Add(Theme.Lead("Measure baseline", delegate { Measure(true); }));
            bar.Controls.Add(Theme.Btn("Measure again (after)", delegate { Measure(false); }));
            bar.Controls.Add(Theme.Btn("Clear results", delegate { outBox.Clear(); before = null; MeasureRuns.Clear(); compare.Reload(); ShowEmpty(); }));

            var pm = Theme.Bar();
            pm.Controls.Add(new Label { Text = "PresentMon — process:", ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
            proc = new TextBox { Width = 160, BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Text = "game.exe", Margin = new Padding(0, 5, 8, 0) };
            secs = new TextBox { Width = 50, BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Text = "15", Margin = new Padding(0, 5, 8, 0) };
            pm.Controls.Add(proc); pm.Controls.Add(new Label { Text = "sec", ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
            pm.Controls.Add(secs); pm.Controls.Add(Theme.Btn("Capture frametimes", delegate { Frames(); }));
            // 3.31: network tests on their own row
            var net = Theme.Bar();
            net.Controls.Add(new Label { Text = "Network:", ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
            net.Controls.Add(Theme.Btn("Network test (ping + jitter)", delegate { NetworkTest(); }));   // 3.5
            net.Controls.Add(Theme.Btn("Bufferbloat test", delegate { BloatTest(); }));   // 3.30

            // 3.14: what to do, while there is nothing to show
            emptyHint = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted, Font = Theme.F, BackColor = Theme.Panel, UseMnemonic = false,
                Text = "", Tag = "notr" };
            Controls.Add(outBox); Controls.Add(emptyHint);
            Controls.Add(Theme.Note("ArchTweaker measures timer, Sleep(1) and scheduling jitter on this PC. Run a baseline, apply tweaks, then run \"after\" to see the change. Frametime capture needs PresentMon.exe next to ArchTweaker and the game running."));
            Controls.Add(compare.Trend);   // 3.17: trend over saved runs
            Controls.Add(compare);   // 3.14: newest run against the saved baseline
            Controls.Add(net);
            Controls.Add(pm);
            Controls.Add(bar);
            ShowEmpty();
        }
        const string HintNone = "No results yet.\n\n1. Click Measure baseline.\n2. Apply tweaks. Restart Windows if ArchTweaker asks.\n3. Click Measure again (after).\n\nThe baseline stays saved after a restart.";
        const string HintSaved = "The cards above show your saved runs.\n\nClick Measure again (after) to compare a new run with the saved baseline.";
        void ShowEmpty()
        {
            bool empty = outBox.TextLength == 0; emptyHint.Visible = empty; outBox.Visible = !empty;
            if (empty) emptyHint.Text = L.T(MeasureRuns.Baseline != null ? HintSaved : HintNone);
        }
        public void OnShow() { if (before == null) before = MeasureRuns.Baseline; compare.Reload(); ShowEmpty(); }
        public void Filter(string q) { }

        void App(string s) { outBox.AppendText(L.T(s) + "\r\n"); ShowEmpty(); }
        void Measure(bool baseline)
        {
            MetricSet m = null;
            MainForm.Instance.Busy("Measuring…", delegate { m = Meter.Capture(300); }, delegate
            {
                if (m == null) { App("Measurement failed — see log."); return; }
                if (baseline) { before = m; MeasureRuns.Save(m, true); App("[baseline] " + m.Line()); }
                else if (before == null) App("Take a baseline first.");
                else { MeasureRuns.Save(m, false); App("[after]    " + m.Line()); App(Meter.Compare(before, m)); App(""); }
                compare.Reload();
            });
        }
        // 3.5: 60 pings to the router and to 1.1.1.1, then where the problem is (LAN or past the router)
        void NetworkTest()
        {
            NetTest.Stat router = null, net = null; string gw = null;
            MainForm.Instance.Busy("Pinging the router and the internet (about 30 s)…", delegate
            {
                gw = NetTest.Gateway();
                if (gw != null) router = NetTest.Run(gw, 60, 200);
                net = NetTest.Run("1.1.1.1", 60, 200);
            }, delegate
            {
                App("[network] " + (router != null ? L.T("router") + " " + router.Line() : L.T("No router (default gateway) found.")));
                App("[network] " + L.T("internet") + " " + net.Line());
                App("[network] " + NetTest.Verdict(router, net));
                App("");
            });
        }
        // 3.30: latency at rest, while downloading and while uploading (about 30 s, moves a few hundred MB on a fast line)
        void BloatTest()
        {
            if (!Dlg.Confirm("Run the bufferbloat test?", "ArchTweaker pings 1.1.1.1 for 10 s at rest, 10 s while it downloads from speed.cloudflare.com and 10 s while it uploads there. It fills your line, so pause other downloads first. On a fast line this moves a few hundred MB.", "Run the test")) return;
            Bufferbloat.Result r = null;
            MainForm.Instance.Busy("Testing latency at rest, while downloading and while uploading (about 30 s)…", delegate { r = Bufferbloat.Run(10); }, delegate
            {
                if (r == null) { App("[bufferbloat] " + L.T("The test failed. Details are in the log.")); return; }
                App("[bufferbloat] " + r.Idle.Line());
                App("[bufferbloat] " + r.Download.Line());
                App("[bufferbloat] " + r.Upload.Line());
                if (r.Grade == "?") App("[bufferbloat] " + L.T(Bufferbloat.Advice(r.Grade)));   // 3.38.3: no baseline, no grade
                else App("[bufferbloat] " + L.F("Grade {0}: +{1:0} ms while downloading, +{2:0} ms while uploading.", r.Grade, r.DownMore, r.UpMore) + " " + L.T(Bufferbloat.Advice(r.Grade)));
                if (r.Download.Mbps < 1 && r.Upload.Mbps < 1) App("[bufferbloat] " + L.T("ArchTweaker could not load the line (no download or upload). The grade does not count."));
                App("");
            });
        }
        void Frames()
        {
            int s; if (!int.TryParse(secs.Text.Trim(), out s)) s = 15;
            string p = proc.Text.Trim(); if (p.Length == 0) { App("Enter a process name, for example game.exe."); return; }
            FrameStats fs = null; string err = null;
            MainForm.Instance.Busy("Capturing " + s + "s of frametimes…", delegate { fs = PresentMon.Capture(p, s, out err); }, delegate
            { App(fs != null ? "[frames " + p + "] " + fs.Describe() : "PresentMon: " + err); });
        }
    }
}
