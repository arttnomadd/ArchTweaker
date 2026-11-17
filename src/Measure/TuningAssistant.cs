// ArchTweaker 3.19: tuning assistant.
// Picks tweaks that can matter on this PC and tests them one by one on the running game (or on system latency
// when no game is chosen). Each tweak is switched off and on in turns (off, on, off, on…), so slow drift such as
// heat and shader caches hits both sides. A tweak stays on only when Welch's t-test says it helped by at least
// 1 %; otherwise it goes back off. Kept tweaks stay on while the next one is tested, so the result builds up.
// The kept set is one apply in the history (Undo last undoes it) and the run writes an HTML report.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    public class TuneStep
    {
        public Tweak T; public List<double> Off = new List<double>(), On = new List<double>();
        public double MeanOff, MeanOn, DiffPct; public bool Significant, Kept, Skipped; public string Note = "";
    }

    public class TuneResult
    {
        public string Game, Metric, Unit, Error, Report; public bool HigherBetter, Cancelled;
        public DateTime When = DateTime.Now; public int Secs, Rounds;
        public List<TuneStep> Steps = new List<TuneStep>();
    }

    public static class Tuner
    {
        public static volatile bool Cancel;
        static readonly HashSet<string> Cats = new HashSet<string> { Cat.Gaming, Cat.Input, Cat.PowerPlan, Cat.Gpu, Cat.Responsiveness, Cat.System };

        // Can this tweak be measured in place? (pure apart from the tweak's own properties; unit-tested)
        public static string Ineligible(Tweak t)
        {
            if (t == null) return "none";
            if (!Cats.Contains(t.Category)) return "not a performance category";
            if (t.Dangerous) return Cat.Danger;
            if (t.Experimental) return "experimental";
            if (t.Risk == Risk.Advanced) return "advanced risk";
            if (t.Reboot) return "needs a restart";
            if (t.Relogon) return "needs a sign-out";
            if (t.Impact < 1) return "no expected effect";
            if (t.UndoNote == "permanent") return "can't be undone";
            if (t.Ops.Count == 0 && t.ApplyExtra == null && !t.HasCommands) return "nothing to switch";
            return null;
        }

        // Off the UI thread: reads tweak states.
        public static List<Tweak> Candidates(int max)
        {
            var applied = new HashSet<string>(Catalog.All.Where(x => x.Applicable && SafeApplied(x)).Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
            var picks = Catalog.All.Where(t => t.Applicable && Ineligible(t) == null && ABTest.Unsupported(t) == null && !applied.Contains(t.Id)
                && !t.Excludes.Any(e => applied.Contains(e)) && !t.CoveredBy.Any(c => applied.Contains(c))).ToList();
            picks = Catalog.Uncovered(picks);
            // one of each group that excludes each other: keep the one with the higher impact
            var o = new List<Tweak>();
            foreach (var t in picks.OrderByDescending(x => x.Impact).ThenBy(x => Catalog.All.IndexOf(x)))
                if (!o.Any(k => k.Excludes.Contains(t.Id) || t.Excludes.Contains(k.Id))) o.Add(t);
            return o.Take(Math.Max(1, max)).ToList();
        }
        static bool SafeApplied(Tweak t) { try { return t.IsApplied(); } catch { return false; } }

        // Keep only on a significant change of at least 1 % in the right direction.
        public static void Decide(TuneStep s, bool higherBetter)
        {
            var c = ABTest.Compare(s.Off.ToArray(), s.On.ToArray());
            s.MeanOff = c.MeanA; s.MeanOn = c.MeanB; s.DiffPct = c.DiffPct; s.Significant = c.Significant;
            bool better = higherBetter ? c.DiffPct > 0 : c.DiffPct < 0;
            s.Kept = c.Significant && better && Math.Abs(c.DiffPct) >= 1.0;
            s.Note = s.Kept ? "helped" : !c.Significant || Math.Abs(c.DiffPct) < 1.0 ? "within noise" : "made it worse";
        }

        // One measurement. With a game: 1 % low FPS (higher is better). Without (3.24): how late a 1 ms high-resolution timer wakes, 99th percentile in µs (lower is better); Sleep(1) p99 in ms only where Windows has no such timer.
        public static double Measure(string game, int secs, out string err)   // 3.24: the power tuner uses it too
        {
            err = null;
            if (game != null)
            {
                var fs = PresentMon.Capture(game, secs, out err);
                return fs == null ? -1 : fs.P1LowFps;
            }
            // 3.24: µs steps, not timer-resolution steps. 3.27.1: no silent switch to Sleep(1) in ms (it mixed units in
            // one run); without the high-resolution timer the run stops and says why.
            string why; var late = WakeLatency.Sample(secs, out why);
            if (late != null && late.Count > 50) return WakeLatency.P99(late);
            err = L.F("Could not measure the timer wake-up delay: {0}. Choose a running game instead.", why ?? L.T("too few samples"));
            return -1;
        }

        public static TuneResult Run(string game, List<Tweak> picks, int secs, int rounds, Action<string> say)
        {
            Cancel = false;
            var res = new TuneResult { Game = game ?? "", Secs = secs, Rounds = rounds, HigherBetter = game != null,
                Metric = game != null ? "1 % low FPS" : "Timer wake-up delay, 99th percentile", Unit = game != null ? "FPS" : "µs" };
            var kept = new List<KeyValuePair<Tweak, bool>>(); bool failed = false;
            try { RegSnapshot.Capture(picks.Select(t => new KeyValuePair<Tweak, bool>(t, true)), "assistant"); } catch (Exception ex) { Log.W("Snapshot: {0}", ex.Message); }
            Log.W("Tuning assistant: {0} tweak(s), {1} × {2} s per side, metric {3}{4}", picks.Count, rounds, secs, res.Metric, game != null ? " of " + game : "");
            for (int n = 0; n < picks.Count; n++)
            {
                var t = picks[n];
                var s = new TuneStep { T = t };
                res.Steps.Add(s);
                if (Cancel || failed) { s.Skipped = true; s.Note = failed ? "not tested (run stopped by an error)" : "not tested (stopped)"; continue; }
                bool wasOn = SafeApplied(t);
                if (wasOn) { s.Skipped = true; s.Note = "already on"; continue; }
                string err = null;
                try
                {
                    Pending(t.Id);   // a crash while the tweak is on: it goes back off at the next start
                    for (int r = 1; r <= rounds && err == null && !Cancel; r++)
                        // ABBA: off-on, then on-off, so warm-up and drift don't always land on the same side
                        foreach (bool on in r % 2 == 1 ? new[] { false, true } : new[] { true, false })
                        {
                            if (Cancel) break;
                            say(L.F("Tweak {0} of {1}: {2} · round {3} of {4}, tweak {5}", n + 1, picks.Count, L.T(t.Title), r, rounds, on ? "ON" : "OFF"));
                            PowerCache.Batch(() => { if (on) t.Apply(); else t.Revert(); });
                            for (int w = 0; w < 8 && !Cancel; w++) Thread.Sleep(500);   // let the change settle
                            if (Cancel) break;
                            double v = Measure(game, secs, out err);
                            if (v <= 0 || double.IsNaN(v) || double.IsInfinity(v)) { err = err ?? "the capture had too few frames"; break; }
                            (on ? s.On : s.Off).Add(v);
                        }
                }
                catch (Exception ex) { err = ex.Message; }
                if (err != null || Cancel || s.On.Count < 2 || s.Off.Count < 2)
                {
                    s.Skipped = true; s.Note = err != null ? "stopped: " + err : "not finished";
                    Restore(t, false); Pending(null);
                    if (err != null && game != null) { res.Error = err; failed = true; }   // the game closed or PresentMon failed: stop the run
                    continue;
                }
                Decide(s, res.HigherBetter);
                // 3.34: store the measurement against the tweak (signed so + always means "improved")
                ProofStore.Record(t, res.Metric, res.Unit, res.HigherBetter ? s.DiffPct : -s.DiffPct, Math.Min(s.On.Count, s.Off.Count), s.Significant, "Tuning assistant");
                if (s.Kept)
                {
                    Restore(t, true);
                    AppliedSet.Mark(t, true);
                    kept.Add(new KeyValuePair<Tweak, bool>(t, true));
                }
                else Restore(t, false);
                Pending(null);
                Log.W("Tuning assistant: {0}: off {1:0.00} → on {2:0.00} {3} ({4:+0.0;-0.0} %) → {5}", t.Title, s.MeanOff, s.MeanOn, res.Unit, s.DiffPct, s.Kept ? "kept" : "back off");
            }
            res.Cancelled = Cancel && !failed;
            if (kept.Count > 0) { History.Push(kept); try { Snapshot.Capture(); } catch (Exception ex) { Log.W("Tuning assistant: What changed snapshot: {0}", ex.Message); } }
            try { res.Report = WriteReport(res); } catch (Exception ex) { Log.W("Tuning report: {0}", ex.Message); }
            return res;
        }

        // HKCU\Software\ArchTweaker\TuneInProgress = id of the tweak under test (it was off before the test)
        static void Pending(string id)
        {
            try
            {
                using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(@"Software\ArchTweaker"))
                    if (id == null) k.DeleteValue("TuneInProgress", false); else k.SetValue("TuneInProgress", id);
            }
            catch (Exception ex) { Log.W("Tuning assistant: progress marker: {0}", ex.Message); }
        }
        // At start: ArchTweaker closed or crashed during a test, so that tweak may still be on. Put it back off.
        public static void Recover()
        {
            try
            {
                string id = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "TuneInProgress") as string;
                if (string.IsNullOrEmpty(id)) return;
                var t = Catalog.All.FirstOrDefault(x => x.Id == id);
                if (t != null) { Restore(t, false); Log.Say("The tuning assistant was interrupted while it tested \"{0}\". ArchTweaker switched it back off.", t.Title); }
                Pending(null);
            }
            catch (Exception ex) { Log.W("Tuning assistant recovery: {0}", ex.Message); }
        }

        static void Restore(Tweak t, bool on)
        {
            try { PowerCache.Batch(() => { if (on) t.Apply(); else t.Revert(); }); }
            catch (Exception ex) { Log.W("Tuning assistant: putting back {0} failed: {1}", t.Title, ex.Message); }
        }

        static string N(double v, string f) { return v.ToString(f, CultureInfo.InvariantCulture); }

        public static string WriteReport(TuneResult r)
        {
            var sb = new StringBuilder();
            sb.Append(ReportHtml.Head("ArchTweaker tuning " + r.When.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
            int k = r.Steps.Count(s => s.Kept);
            sb.Append("<h1>Tuning assistant</h1><div class=mut>" + ReportHtml.Esc(r.When.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)) + " · ArchTweaker " + ReportHtml.Esc(Program.Version)
                + (r.Game.Length > 0 ? " · " + ReportHtml.Esc(r.Game) : " · system latency") + " · " + r.Rounds + " × " + r.Secs + " s per side</div>");
            sb.Append("<p>" + k + " of " + r.Steps.Count + " tweaks helped and stay on. Metric: " + ReportHtml.Esc(r.Metric) + (r.HigherBetter ? " (higher is better)" : " (lower is better)")
                + ". A tweak stays on only when the change is at least 1 % and passes Welch's t-test at 95 %. Each tweak was tested on top of the ones kept before it."
                + (r.Cancelled ? " <span class=bad>The run was stopped before the end.</span>" : "") + (r.Error != null ? " <span class=bad>" + ReportHtml.Esc(r.Error) + "</span>" : "") + "</p>");
            sb.Append("<table><tr><th>Tweak</th><th>Category</th><th class=n>Off</th><th class=n>On</th><th class=n>Change</th><th>Result</th></tr>");
            foreach (var s in r.Steps)
            {
                string cls = s.Kept ? "ok" : s.Note == "made it worse" ? "bad" : "mut";
                sb.Append("<tr><td>" + ReportHtml.Esc(s.T.Title) + "</td><td class=mut>" + ReportHtml.Esc(s.T.Category) + "</td>"
                    + (s.Skipped ? "<td class=n>—</td><td class=n>—</td><td class=n>—</td>"
                        : "<td class=n>" + N(s.MeanOff, "0.00") + "</td><td class=n>" + N(s.MeanOn, "0.00") + "</td><td class=n>" + N(s.DiffPct, "+0.0;-0.0") + " %</td>")
                    + "<td class=" + cls + ">" + ReportHtml.Esc(s.Kept ? "kept on" : s.Note) + "</td></tr>");
            }
            sb.Append("</table><p class=mut>Undo last (or Change history) takes the kept tweaks back off in one step.</p></body></html>");
            string file = Path.Combine(Bench.ReportDir, "tuning_" + r.When.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".html");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            return file;
        }
    }

    public class TunerPage : Panel, IPage
    {
        readonly ComboBox game = Theme.Drop(240, "Game", 8);
        readonly ComboBox secs = Theme.Drop(70, "Capture length");
        readonly ComboBox rounds = Theme.Drop(60, "Rounds");
        readonly ComboBox max = Theme.Drop(60, "Most tweaks");
        readonly ListView lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, CheckBoxes = true };
        readonly Button findBtn, runBtn, stopBtn, reportBtn;
        readonly Label status, estimate;
        bool running; TuneResult last;
        const string NoGame = "No game: system latency only";

        public TunerPage()
        {
            BackColor = Theme.Bg;
            Theme.DarkList(lv);
            lv.Columns.Add("Tweak", 300); lv.Columns.Add("Category", 140); lv.Columns.Add("Expected", 80); lv.Columns.Add("Off", 80); lv.Columns.Add("On", 80); lv.Columns.Add("Change", 80); lv.Columns.Add("Result", 160);
            lv.ItemChecked += delegate { Estimate(); };

            var bar = Theme.Bar();
            bar.Controls.Add(Theme.BarLabel("Game:")); bar.Controls.Add(game);
            bar.Controls.Add(Theme.Btn("Refresh", delegate { FillGames(); }));
            bar.Controls.Add(Theme.BarLabel("Per side:")); secs.Items.AddRange(new object[] { "20 s", "30 s", "60 s" }); secs.SelectedIndex = 1; bar.Controls.Add(secs);
            bar.Controls.Add(Theme.BarLabel("Rounds:")); rounds.Items.AddRange(new object[] { "2", "3", "4" }); rounds.SelectedIndex = 0; bar.Controls.Add(rounds);
            bar.Controls.Add(Theme.BarLabel("Most:")); max.Items.AddRange(new object[] { "5", "8", "12", "20" }); max.SelectedIndex = 1; bar.Controls.Add(max);
            secs.SelectedIndexChanged += delegate { Estimate(); }; rounds.SelectedIndexChanged += delegate { Estimate(); };

            var bar2 = Theme.Bar();
            findBtn = Theme.Btn("Find tweaks to test", delegate { Find(); });
            runBtn = Theme.Lead("Start tuning", delegate { Run(); }); runBtn.Enabled = false;
            stopBtn = Theme.Btn("Stop", delegate { Tuner.Cancel = true; status.Text = L.T("Stopping after this measurement…"); }); stopBtn.Enabled = false;
            reportBtn = Theme.Btn("Open report", delegate { if (last != null && last.Report != null && File.Exists(last.Report)) Shell.OpenVisible("explorer.exe", "\"" + last.Report + "\""); }); reportBtn.Enabled = false;
            bar2.Controls.Add(findBtn); bar2.Controls.Add(runBtn); bar2.Controls.Add(stopBtn); bar2.Controls.Add(reportBtn);
            status = Theme.StatusText();
            bar2.Controls.Add(status);
            estimate = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 22, ForeColor = Theme.Muted, Font = Theme.FS, UseMnemonic = false, Tag = "notr", Padding = new Padding(2, 4, 2, 0) };

            Controls.Add(lv); Controls.Add(estimate);
            Controls.Add(Theme.Note("ArchTweaker tests each tweak on this PC: off, on, off, on… and keeps it only when it measurably helped. With a game it compares 1 % low FPS (needs PresentMon); without one, Sleep(1) timer precision. Keep the game in one repeatable scene."));
            Controls.Add(bar2); Controls.Add(bar);
            Skin.SetEmpty(lv, "Click Find tweaks to test. ArchTweaker lists tweaks that are off, take effect at once and can matter for games.");
        }

        public void OnShow() { if (!running) FillGames(); }
        public void Filter(string q) { }

        void FillGames()
        {
            string keep = game.SelectedItem as string;
            game.Items.Clear(); game.Items.Add(L.T(NoGame));
            foreach (var g in Bench.Games()) game.Items.Add(g);
            int i = keep == null ? -1 : game.Items.IndexOf(keep);
            game.SelectedIndex = i >= 0 ? i : 0;
        }
        int Secs { get { return secs.SelectedIndex == 0 ? 20 : secs.SelectedIndex == 2 ? 60 : 30; } }
        int Rounds { get { return rounds.SelectedIndex + 2; } }
        List<Tweak> Checked() { return lv.Items.Cast<ListViewItem>().Where(i => i.Checked && i.Tag is Tweak).Select(i => (Tweak)i.Tag).ToList(); }

        void Estimate()
        {
            int n = Checked().Count;
            int s = n * Rounds * 2 * (Secs + 6);
            estimate.Text = n == 0 ? "" : L.F("{0} tweak(s) · about {1} min. Stay in the game while it runs. ArchTweaker switches the tweaks by itself.", n, Math.Max(1, (s + 59) / 60));
            runBtn.Enabled = n > 0 && !running;
        }

        void Find()
        {
            int m = int.Parse((string)max.SelectedItem, CultureInfo.InvariantCulture);
            List<Tweak> c = null;
            findBtn.Enabled = false;
            MainForm.Instance.Busy("Finding tweaks to test…", delegate { c = Tuner.Candidates(m); }, delegate
            {
                findBtn.Enabled = true;
                lv.BeginUpdate(); lv.Items.Clear();
                foreach (var t in c ?? new List<Tweak>())
                    lv.Items.Add(new ListViewItem(new[] { L.T(t.Title), L.T(t.Category), t.ImpactName, "", "", "", "" }) { Tag = t, Checked = true });
                lv.EndUpdate();
                Skin.Loaded(lv, "No tweak left to test: the ones that can matter are on already, or need a restart.");
                Estimate();
            });
        }

        void Run()
        {
            if (running) return;
            var picks = Checked(); if (picks.Count == 0) return;
            string g = game.SelectedIndex > 0 ? game.SelectedItem as string : null;
            if (g != null && !PresentMon.Available) { Dlg.Error("PresentMon.exe not found", "Choose PresentMon.exe on the Benchmark page, or choose \"No game\" to tune for system latency."); return; }
            if (!Dlg.Confirm("Start the tuning assistant?",
                L.F("ArchTweaker switches {0} tweak(s) on and off while it measures, and keeps only the ones that helped. Go back to the game and stay in one scene. It takes about {1} min. Stop puts the current tweak back.", picks.Count, Math.Max(1, (picks.Count * Rounds * 2 * (Secs + 6) + 59) / 60)),
                "Start tuning")) return;
            running = true; runBtn.Enabled = false; findBtn.Enabled = false; stopBtn.Enabled = true; reportBtn.Enabled = false;
            foreach (ListViewItem i in lv.Items) { i.SubItems[3].Text = i.SubItems[4].Text = i.SubItems[5].Text = ""; i.SubItems[6].Text = i.Checked ? L.T("waiting") : ""; }
            int secsV = Secs, roundsV = Rounds;
            Action<string> say = Theme.SayTo(this, status);
            TuneResult res = null;
            MainForm.Instance.Busy("Tuning…", delegate { res = Tuner.Run(g, picks, secsV, roundsV, say); }, delegate
            {
                running = false; stopBtn.Enabled = false; findBtn.Enabled = true; status.Text = "";
                last = res; reportBtn.Enabled = res != null && res.Report != null;
                if (res == null) { Dlg.Error("The tuning assistant stopped", "Details are in the log: " + Paths.LogFile); Estimate(); return; }
                foreach (ListViewItem i in lv.Items)
                {
                    var s = res.Steps.FirstOrDefault(x => x.T == i.Tag); if (s == null) continue;
                    string f = res.HigherBetter ? "0.0" : "0.00";
                    i.SubItems[3].Text = s.Skipped ? "—" : s.MeanOff.ToString(f);
                    i.SubItems[4].Text = s.Skipped ? "—" : s.MeanOn.ToString(f);
                    i.SubItems[5].Text = s.Skipped ? "—" : s.DiffPct.ToString("+0.0;-0.0") + " %";
                    i.SubItems[6].Text = L.T(s.Kept ? "kept on" : s.Note);
                    i.Checked = false;
                }
                int k = res.Steps.Count(x => x.Kept);
                Log.Say("Tuning assistant: {0} of {1} tweak(s) helped and stay on.{2}", k, res.Steps.Count, res.Error != null ? " " + res.Error : "");
                MainForm.Instance.RefreshAll();
                Estimate();
                if (res.Report != null) Shell.OpenVisible("explorer.exe", "\"" + res.Report + "\"");
            });
        }
    }
}
