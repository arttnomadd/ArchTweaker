// v2.7 page: A/B test (prove one tweak with PresentMon).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    public class ABTestPage : Panel, IPage
    {
        readonly ComboBox tweak, rtweak;   // 3.7: rtweak = restart A/B test
        List<Tweak> rchoices;
        readonly TextBox proc, secs, rounds, outBox;
        List<Tweak> choices;

        public ABTestPage()
        {
            BackColor = Theme.Bg;
            outBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Mono, AccessibleName = "A/B results" };
            var bar = Theme.Bar();
            bar.Controls.Add(L("Tweak:"));
            tweak = Theme.Drop(380, "Tweak to test");
            bar.Controls.Add(tweak);
            bar.Controls.Add(L("Game process:"));
            proc = T("game.exe", 130, "Game process name");
            bar.Controls.Add(proc);
            var bar2 = Theme.Bar();
            bar2.Controls.Add(L("Seconds per run:")); secs = T("20", 44, "Seconds per run"); bar2.Controls.Add(secs);
            bar2.Controls.Add(L("Runs each:")); rounds = T("3", 34, "Runs each"); bar2.Controls.Add(rounds);
            bar2.Controls.Add(Theme.Lead("Run A/B test", delegate { Run(); }));
            bar2.Controls.Add(Theme.Btn("Clear results", delegate { outBox.Clear(); }));
            Controls.Add(outBox);
            Controls.Add(Theme.Note("Start the game and stand somewhere repeatable (a menu, a benchmark scene, an empty map). ArchTweaker switches the tweak off and on in turn, " +
                "capturing frame times each time with PresentMon, and says whether the difference is bigger than the run-to-run noise. The tweak ends as it was before. " +
                "The list shows only tweaks that work without a restart. Needs PresentMon.exe next to ArchTweaker."));
            Controls.Add(bar2);
            Controls.Add(bar);
            // 3.7: tweaks that need a restart: measured idle (DPC / interrupt time, Sleep(1) precision) after a restart, before and after
            var bar3 = Theme.Bar();
            bar3.Controls.Add(L("Restart test:"));
            rtweak = Theme.Drop(380, "Tweak for the restart test");
            bar3.Controls.Add(rtweak);
            bar3.Controls.Add(Theme.Btn("Start restart test", delegate { RunRestart(); }));
            Controls.Add(Theme.Note(ArchTweaker.L.F("Restart test, for tweaks that need a restart and experimental tweaks. No game needed. ArchTweaker restarts and measures DPC time, interrupt time and timer precision while the PC is idle. Then it applies the tweak, restarts again, measures once more and tells you if it helped. ArchTweaker opens by itself after each restart and measures {0} s later.", RebootAB.SettleSeconds)));
            Controls.Add(bar3);
        }
        static bool ValidExe(string exe)
        {
            return !string.IsNullOrEmpty(exe) && exe.Length < 120 && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && exe.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && exe.IndexOfAny(new[] { '\\', '/', ':' }) < 0;
        }
        static Label L(string t) { return new Label { Text = t, ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = true, Margin = new Padding(0, 8, 4, 0) }; }
        static TextBox T(string v, int w, string name) { return new TextBox { Text = v, Width = w, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 5, 10, 0), AccessibleName = name }; }

        public void OnShow()
        {
            if (choices != null) return;
            // biggest expected gains first, so the tweaks worth proving are at the top
            choices = Catalog.All.Where(t => ABTest.Unsupported(t) == null && t.Impact > 0).OrderByDescending(t => t.Impact).ThenBy(t => t.Category).ThenBy(t => t.Title).ToList();
            tweak.Items.AddRange(choices.Select(t => (object)(t.Category + "  ·  " + t.Title)).ToArray());
            if (tweak.Items.Count > 0) tweak.SelectedIndex = 0;
            rchoices = Catalog.All.Where(t => RebootAB.Testable(t) && (t.Reboot || t.Experimental)).OrderByDescending(t => t.Experimental).ThenBy(t => t.Category).ThenBy(t => t.Title).ToList();
            rtweak.Items.AddRange(rchoices.Select(t => (object)((t.Experimental ? "★ " : "") + t.Category + "  ·  " + t.Title)).ToArray());
            if (rtweak.Items.Count > 0) rtweak.SelectedIndex = 0;
        }
        public void Filter(string q) { }
        // 3.34: open from a tweak's details pane with that tweak already chosen
        public void Preselect(Tweak t)
        {
            OnShow();
            int i = choices == null ? -1 : choices.FindIndex(x => x.Id == t.Id);
            if (i >= 0) tweak.SelectedIndex = i;
        }
        void App(string s) { outBox.AppendText(s + "\r\n"); }

        void RunRestart()
        {
            if (rtweak.SelectedIndex < 0) return;
            var t = rchoices[rtweak.SelectedIndex];
            if (RebootAB.PendingId != null) { Dlg.Error("A restart test is already running", "Open ArchTweaker after the restart and wait for the result, or remove HKCU\\Software\\ArchTweaker\\RebootAB to cancel it."); return; }
            if (StateCache.Applied(t)) { Dlg.Error("This tweak is already on", "Undo it first, then start the test, so the first measurement is without it."); return; }
            if (t.Reboot)
            {
                if (!Dlg.Confirm("Start the restart test?", ArchTweaker.L.F("ArchTweaker restarts the PC, measures it idle, applies '{0}', restarts again and measures once more. After each restart, ArchTweaker opens by itself when you sign in. Leave the PC idle. Each measurement starts {1} s later and takes about a minute. Save your work first.", ArchTweaker.L.T(t.Title), RebootAB.SettleSeconds), "Restart now")) return;
                RebootAB.Begin(t);
                Shell.Run("shutdown.exe", "/r /t 5", false);
                return;
            }
            if (!Dlg.Confirm("Start the A/B test?", ArchTweaker.L.F("ArchTweaker measures the PC idle, applies '{0}' and measures again. It takes about 2 minutes. Leave the PC idle.", ArchTweaker.L.T(t.Title)), "Start")) return;
            string v = null; bool worse = false;
            AppShell.Current.Busy("Measuring…", () =>
            {
                var a = RebootAB.Measure(null); t.Apply(); System.Threading.Thread.Sleep(5000); var b = RebootAB.Measure(null);
                bool better; v = RebootAB.Verdict(a, b, out worse, out better);
            }, () => { ShowResult(t, v, worse); AppShell.Current.RefreshAll(); });
        }

        // shown after a restart test finishes (also from the main window after a restart)
        public static void ShowResult(Tweak t, string verdict, bool worse)
        {
            Log.Say("A/B test '{0}':\r\n{1}", ArchTweaker.L.T(t.Title), verdict);
            string head = worse ? ArchTweaker.L.T("It made at least one measurement worse. Undo it?") : ArchTweaker.L.T("Keep the tweak, or undo it?");
            if (Dlg.Confirm(ArchTweaker.L.F("A/B test result: {0}", ArchTweaker.L.T(t.Title)), verdict + "\r\n" + head, "Undo the tweak", "Keep it"))
            {
                t.Revert(); StateCache.Clear();
                AppShell.Current.Notify(t.Reboot ? ArchTweaker.L.T("Undone. Restart to finish.") : ArchTweaker.L.T("Undone."), Toast.Kind.Ok);
            }
            AppShell.Current.RefreshAll();
        }

        void Run()
        {
            if (tweak.SelectedIndex < 0) return;
            var t = choices[tweak.SelectedIndex];
            int s, n;
            if (!int.TryParse(secs.Text.Trim(), out s) || s < 5 || s > 120) { Dlg.Error("Seconds per run must be 5–120", "Longer runs give steadier numbers. 20 s is a good start."); return; }
            if (!int.TryParse(rounds.Text.Trim(), out n) || n < 2 || n > 8) { Dlg.Error("Runs each must be 2–8", "At least 2 runs per side are needed to tell a real change from noise."); return; }
            string p = proc.Text.Trim();
            if (!ValidExe(p)) { Dlg.Error("Enter the game's process name", "For example cs2.exe, as shown in Task Manager → Details."); return; }
            if (!PresentMon.Available) { Dlg.Error("PresentMon.exe not found", "Put PresentMon.exe next to ArchTweaker.exe (download it from github.com/GameTechDev/PresentMon), then try again."); return; }
            int minutes = (int)Math.Ceiling(n * 2 * (s + 8) / 60.0);
            if (!Dlg.Confirm("Run the A/B test for about " + minutes + " min?", ArchTweaker.L.F("Keep the game in the same scene the whole time. ArchTweaker turns \"{0}\" off {1} times and on {1} times, then puts it back as it is now.", ArchTweaker.L.T(t.Title), n), "Start test", "Cancel")) return;
            ABResult r = null;
            App("— " + DateTime.Now.ToString("HH:mm") + "  " + t.Title);
            AppShell.Current.Busy("A/B test running…", delegate
            {
                r = ABTest.Run(t, p, s, n, line => { try { BeginInvoke((Action)(() => App("  " + line))); } catch { } });
            }, delegate
            {
                App(r.Report(t.Title)); App("");
                if (r.Error == null && r.On.Count >= 2 && r.Off.Count >= 2)   // 3.34: prove it on this PC (1 % low FPS)
                {
                    var c = ABTest.Compare(r.Off.Select(f => f.P1LowFps).ToArray(), r.On.Select(f => f.P1LowFps).ToArray());
                    ProofStore.Record(t, "1 % low FPS", "FPS", c.DiffPct, Math.Min(r.On.Count, r.Off.Count), c.Significant, "A/B test");
                }
                MainForm.Instance.RefreshAfterExternalChange();
            });
        }
    }
}
