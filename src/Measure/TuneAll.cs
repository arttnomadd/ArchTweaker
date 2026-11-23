// ArchTweaker: Tune everything (Pro): the Interrupt tuner, the Power tuner and the Tuning assistant one after another,
// with one summary report that links the three tool reports. Each tool keeps only what measurably helped.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ArchTweaker
{
    public class TuneAllResult
    {
        public DateTime When = DateTime.Now; public string Game; public int Secs, Rounds;
        public IrqTuneResult Irq; public List<PowerTrial> Power; public TuneResult Tweaks;
        public string PowerReport, Error, Report; public bool Cancelled;
        public List<string> Notes = new List<string>();   // 3.27.1: why a step was skipped or failed
        public int IrqKept { get { return Irq == null ? 0 : Irq.Devices.Sum(d => d.Trials.Count(t => t.Kept)); } }
        public int PowerKept { get { return Power == null ? 0 : Power.Count(t => t.Kept); } }
        public int TweaksKept { get { return Tweaks == null ? 0 : Tweaks.Steps.Count(s => s.Kept); } }
    }

    public static class TuneAll
    {
        public static volatile bool Cancel;
        public static void Stop() { Cancel = true; IrqTuner.Cancel = true; PowerTuner.Cancel = true; Tuner.Cancel = true; }

        // The interrupt tuner's default ticks: no graphics card (its restart closes games), only devices with something to try.
        public static List<IrqDevice> IrqDefaults()
        {
            var c = HwProbe.Cpu; var cpus = c == null ? new List<int>() : IrqTuner.PinCpus(c.FastMask, c.Smt, c.Threads);
            return Irq.List().Where(d => (d.Kind == "Network" || d.Kind == "USB" || d.Kind == "Audio") && IrqTuner.Configs(d, cpus).Count > 0).ToList();
        }

        public static TuneAllResult Run(string game, int secs, int rounds, bool irq, bool power, bool tweaks, Action<string> say)
        {
            Cancel = false;
            var r = new TuneAllResult { Game = game, Secs = secs, Rounds = rounds };
            Log.W("Tune everything: interrupts {0}, power {1}, tweaks {2}; {3} × {4} s", irq, power, tweaks, rounds, secs);
            if (irq && !Cancel)
            {
                say(L.T("Step 1 of 3: Interrupt tuner"));
                try
                {
                    var devs = IrqDefaults();
                    if (devs.Count > 0) r.Irq = IrqTuner.Run(devs, Math.Min(secs, 10), rounds, say);
                    else r.Notes.Add(L.T("Interrupt tuner skipped: no network, USB or audio device has a setting to try."));
                }
                catch (Exception ex) { Log.W("Tune everything, interrupts: {0}", ex.Message); r.Notes.Add(L.F("Interrupt tuner failed: {0}", ex.Message)); }
            }
            if (power && !Cancel)
            {
                say(L.T("Step 2 of 3: Power tuner"));
                string err; r.Power = PowerTuner.Run(game, secs, rounds, say, out err); r.PowerReport = PowerTuner.LastReport;
                if (err != null) r.Error = err;
            }
            if (tweaks && !Cancel && r.Error == null)
            {
                say(L.T("Step 3 of 3: Tuning assistant"));
                var picks = Tuner.Candidates(5);
                if (picks.Count > 0) r.Tweaks = Tuner.Run(game, picks, secs, rounds, say);
                else r.Notes.Add(L.T("Tuning assistant skipped: every tweak it can test is already tested or applied."));
            }
            else if (tweaks && !Cancel && r.Error != null) r.Notes.Add(L.T("Tuning assistant skipped because the power tuner stopped with an error."));
            r.Cancelled = Cancel;
            try { r.Report = WriteReport(r); } catch (Exception ex) { Log.Say("Could not save the report: {0}", ex.Message); }
            Log.W("Tune everything: kept {0} interrupt setting(s), {1} power value(s), {2} tweak(s){3}", r.IrqKept, r.PowerKept, r.TweaksKept, r.Cancelled ? " (stopped)" : "");
            return r;
        }

        static string Link(string file, string text) { return file != null && File.Exists(file) ? "<a href=\"" + ReportHtml.Esc(Path.GetFileName(file)) + "\">" + ReportHtml.Esc(text) + "</a>" : "<span class=mut>no report</span>"; }

        public static string WriteReport(TuneAllResult r)
        {
            var sb = new StringBuilder();
            sb.Append(ReportHtml.Head("ArchTweaker tune everything " + r.When.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
            sb.Append("<h1>Tune everything</h1><div class=mut>" + ReportHtml.Esc(r.When.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)) + " · ArchTweaker " + ReportHtml.Esc(Program.Version)
                + " · " + (r.Game != null ? ReportHtml.Esc(r.Game) : "no game: timer wake-up delay") + " · " + r.Rounds + " × " + r.Secs + " s per side</div>");
            if (r.Cancelled) sb.Append("<p class=bad>You stopped the run before the end.</p>");
            if (r.Error != null) sb.Append("<p class=bad>" + ReportHtml.Esc(r.Error) + "</p>");
            foreach (var n in r.Notes) sb.Append("<p class=mut>" + ReportHtml.Esc(n) + "</p>");
            sb.Append("<table><tr><th>Step</th><th class=n>Kept</th><th>Report</th></tr>");
            sb.Append("<tr><td>1. Interrupt tuner</td><td class=n>" + (r.Irq == null ? "—" : r.IrqKept + " setting(s)") + "</td><td>" + Link(r.Irq == null ? null : r.Irq.Report, "Interrupt tuner report") + "</td></tr>");
            sb.Append("<tr><td>2. Power tuner</td><td class=n>" + (r.Power == null ? "—" : r.PowerKept + " value(s)") + "</td><td>" + Link(r.PowerReport, "Power tuner report") + "</td></tr>");
            sb.Append("<tr><td>3. Tuning assistant</td><td class=n>" + (r.Tweaks == null ? "—" : r.TweaksKept + " tweak(s)") + "</td><td>" + Link(r.Tweaks == null ? null : r.Tweaks.Report, "Tuning assistant report") + "</td></tr>");
            sb.Append("</table><p class=mut>Results → Put back everything the tuners changed restores the interrupt settings and power values. Undo last takes back the kept tweaks.</p></body></html>");
            string file = Path.Combine(Bench.ReportDir, "tuneall_" + r.When.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".html");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            return file;
        }
    }

    public class TuneAllPage : Panel, IPage
    {
        readonly ComboBox game = Theme.Drop(240, "Game", 8);
        readonly ComboBox secs = Theme.Drop(70, "Seconds per sample");
        readonly CheckBox irq = new CheckBox { Text = "Interrupt tuner", Checked = true, AutoSize = true, Margin = new Padding(0, 7, 14, 0) };
        readonly CheckBox power = new CheckBox { Text = "Power tuner", Checked = true, AutoSize = true, Margin = new Padding(0, 7, 14, 0) };
        readonly CheckBox tweaks = new CheckBox { Text = "Tuning assistant", Checked = true, AutoSize = true, Margin = new Padding(0, 7, 14, 0) };
        readonly Button runBtn, stopBtn, reportBtn;
        readonly Label status, summary;
        string lastReport; bool running;

        public TuneAllPage()
        {
            BackColor = Theme.Bg;
            var bar = Theme.Bar();
            bar.Controls.Add(Theme.BarLabel("Game:")); bar.Controls.Add(game);
            bar.Controls.Add(Theme.BarLabel("Seconds per sample:"));
            secs.Items.AddRange(new object[] { "10 s", "20 s", "30 s" }); secs.SelectedIndex = 1; bar.Controls.Add(secs);
            var bar1 = Theme.Bar();
            foreach (var c in new[] { irq, power, tweaks }) { c.ForeColor = Theme.Text; c.Font = Theme.FS; bar1.Controls.Add(c); }
            var bar2 = Theme.Bar();
            runBtn = Theme.Lead("Start", delegate { Run(); }); bar2.Controls.Add(runBtn);
            stopBtn = Theme.Btn("Stop", delegate { TuneAll.Stop(); status.Text = L.T("Stopping after this sample…"); }); stopBtn.Enabled = false; bar2.Controls.Add(stopBtn);
            reportBtn = Theme.Btn("Open report", delegate { if (lastReport != null && File.Exists(lastReport)) Shell.OpenVisible("explorer.exe", "\"" + lastReport + "\""); }); reportBtn.Enabled = false; bar2.Controls.Add(reportBtn);
            status = Theme.StatusText();
            bar2.Controls.Add(status);
            summary = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Text, Font = Theme.FB, Padding = new Padding(4, 12, 4, 4), UseMnemonic = false, Tag = "notr" };
            Controls.Add(summary);
            Controls.Add(Theme.Note("Keep the PC as you use it while it runs. With a game, stay in one scene. The network drops for a moment each time a network adapter restarts."));
            Controls.Add(Theme.Note("Runs the Interrupt tuner (network, USB and audio devices), the Power tuner and the Tuning assistant (5 untested tweaks), one after another. Each step keeps only what measurably helps."));
            Controls.Add(bar2); Controls.Add(bar1); Controls.Add(bar);
        }
        public void OnShow()
        {
            if (running) return;
            Bench.FillGameBox(game, "No game: timer wake-up delay");
        }
        public void Filter(string q) { }

        void Run()
        {
            if (running || !(irq.Checked || power.Checked || tweaks.Checked)) return;
            string g = game.SelectedIndex > 0 ? game.SelectedItem as string : null;
            if (g != null && !PresentMon.Available) { Dlg.Error("PresentMon.exe not found", "Choose PresentMon.exe on the Benchmark page, or choose \"No game\"."); return; }
            int s = secs.SelectedIndex == 0 ? 10 : secs.SelectedIndex == 2 ? 30 : 20;
            if (!Dlg.Confirm("Tune everything?", L.F("This takes about {0} min. Devices restart and tweaks switch on and off while ArchTweaker measures. Stop puts the current setting back.", Math.Max(5, s * 2 * 2 * 14 / 60)), "Start", "Cancel")) return;
            bool a = irq.Checked, b = power.Checked, c = tweaks.Checked;
            running = true; runBtn.Enabled = false; stopBtn.Enabled = true; reportBtn.Enabled = false; summary.Text = "";
            TuneAllResult res = null;
            Action<string> say = Theme.SayTo(this, status);
            AppShell.Current.Busy("Tuning everything…", delegate { res = TuneAll.Run(g, s, 2, a, b, c, say); }, delegate
            {
                running = false; runBtn.Enabled = true; stopBtn.Enabled = false; status.Text = "";
                if (res == null) return;
                lastReport = res.Report; reportBtn.Enabled = lastReport != null;
                summary.Text = L.F("Kept: {0} interrupt setting(s), {1} power value(s), {2} tweak(s).", res.IrqKept, res.PowerKept, res.TweaksKept)
                    + (res.Cancelled ? "  " + L.T("You stopped the run before the end.") : "") + (res.Error != null ? "  " + res.Error : "")
                    + (res.Notes.Count > 0 ? "\r\n" + string.Join("\r\n", res.Notes.ToArray()) : "");
                Log.Say("Tune everything: {0}", summary.Text);
                AppShell.Current.RefreshAll();
            });
        }
    }
}
