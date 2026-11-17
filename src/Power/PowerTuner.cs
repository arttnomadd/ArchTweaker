// ArchTweaker: Power tuner: processor power-plan trials that keep only measured wins, and its page.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ArchTweaker
{
    public class PowerStep
    {
        public string Name = "";
        public string[] Aliases;                              // set together (for example both thresholds)
        public List<uint[]> Values = new List<uint[]>();      // candidates, one value per alias
    }

    public class PowerTrial
    {
        public string Step = "", Value = ""; public List<double> Base = new List<double>(), Try = new List<double>();
        public double MeanBase, MeanTry, DiffPct; public bool Kept; public string Note = "";
    }

    public static class PowerTuner
    {
        public static volatile bool Cancel;
        static string BackupFile { get { return Path.Combine(Paths.Root, "power-tune-backup.tsv"); } }

        public static List<PowerStep> Steps()
        {
            return new List<PowerStep>
            {
                new PowerStep { Name = "Performance time check interval", Aliases = new[] { "PERFCHECK" }, Values = { new uint[] { 5 }, new uint[] { 15 }, new uint[] { 5000 } } },
                new PowerStep { Name = "Performance increase and decrease thresholds", Aliases = new[] { "PERFINCTHRESHOLD", "PERFDECTHRESHOLD" }, Values = { new uint[] { 10, 8 }, new uint[] { 30, 60 } } },
                new PowerStep { Name = "Latency hint performance", Aliases = new[] { "LATENCYHINTPERF" }, Values = { new uint[] { 100 } } },
                new PowerStep { Name = "Minimum processor state", Aliases = new[] { "PROCTHROTTLEMIN" }, Values = { new uint[] { 100 } } },
            };
        }

        // Kept: significant and better by at least 1 % (FPS, higher is better) or 3 % (wake-up delay, lower is better).
        public static void Decide(PowerTrial t, bool higherBetter)
        {
            var c = ABTest.Compare(t.Base.ToArray(), t.Try.ToArray());
            t.MeanBase = c.MeanA; t.MeanTry = c.MeanB; t.DiffPct = c.DiffPct;
            double need = higherBetter ? 1.0 : 3.0;
            bool better = higherBetter ? c.DiffPct >= need : c.DiffPct <= -need;
            bool worse = higherBetter ? c.DiffPct <= -need : c.DiffPct >= need;
            t.Kept = c.Significant && better;
            t.Note = t.Kept ? "kept" : c.Significant && worse ? "made it worse" : "within noise";
        }

        // Only the settings this tuner tries: the alias goes into a powercfg command line, so the backup file cannot add others.
        public static bool KnownAlias(string a) { return Steps().Any(st => st.Aliases.Contains(a)); }

        public static uint? Read(string alias) { bool noAnswer; return Read(alias, out noAnswer); }
        // noAnswer: powercfg printed nothing (failed or timed out), as opposed to a plan without this setting.
        public static uint? Read(string alias, out bool noAnswer)
        {
            string o = Shell.Run("powercfg.exe", "/qh scheme_current sub_processor " + alias, true, 15000);
            noAnswer = string.IsNullOrEmpty(o) || o.Trim().Length == 0;
            var v = HwAudit.LastTwoHex(o);
            return v == null || v.Length == 0 ? (uint?)null : (uint)v[0];
        }
        // 3.27.1: true only when powercfg reads the new value back
        static bool Write(string alias, uint v)
        {
            if (!KnownAlias(alias)) { Log.W("Power tuner: refused unknown setting {0}", alias); return false; }
            Shell.Run("powercfg.exe", "/setacvalueindex scheme_current sub_processor " + alias + " " + v.ToString(CultureInfo.InvariantCulture), true, 15000);
            var back = Read(alias);
            if (back == v) return true;
            Log.W("Power tuner: {0} did not change to {1} (reads {2})", alias, v, back.HasValue ? back.Value.ToString(CultureInfo.InvariantCulture) : "nothing");
            return false;
        }
        static void Activate() { Shell.Run("powercfg.exe", "/setactive scheme_current", true, 15000); }
        static bool Set(string[] aliases, uint[] vals)
        {
            bool ok = true;
            for (int i = 0; i < aliases.Length; i++) ok &= Write(aliases[i], vals[i]);
            Activate(); Thread.Sleep(500);
            return ok;
        }

        static bool BackupTrusted() { return File.Exists(BackupFile) && Secure.TrustedData(BackupFile, "the power tuner backup"); }

        // 3.27.1: false when the original could not be saved; the run then stops before it changes anything
        static bool SaveOriginal(string alias, uint v)
        {
            try
            {
                Secure.DropUntrusted(BackupFile, "the power tuner backup");
                var lines = File.Exists(BackupFile) ? File.ReadAllLines(BackupFile).ToList() : new List<string>();
                if (lines.Any(l => l.StartsWith(alias + "\t", StringComparison.Ordinal))) return true;   // the first original stays
                lines.Add(alias + "\t" + v.ToString(CultureInfo.InvariantCulture)); File.WriteAllLines(BackupFile, lines);
                return true;
            }
            catch (Exception ex) { Log.W("Power tuner backup: {0}", ex.Message); return false; }
        }
        public static bool HasBackup { get { try { return File.Exists(BackupFile) && File.ReadAllLines(BackupFile).Any(l => l.Contains("\t")); } catch { return false; } } }

        static string PendingFile { get { return Path.Combine(Paths.Root, "power-tune-pending.tsv"); } }
        static void Pending(string[] aliases, uint[] vals)
        {
            try
            {
                if (aliases == null) { if (File.Exists(PendingFile)) File.Delete(PendingFile); return; }
                File.WriteAllLines(PendingFile, aliases.Select((a, i) => a + "\t" + vals[i].ToString(CultureInfo.InvariantCulture)).ToArray());
            }
            catch (Exception ex) { Log.W("Power tuner: progress marker: {0}", ex.Message); }
        }
        // At start: ArchTweaker closed during a trial. The setting under test goes back to the value it had before the trial.
        public static void Recover()
        {
            try
            {
                if (!File.Exists(PendingFile)) return;
                if (!Secure.TrustedData(PendingFile, "the power tuner progress marker")) { File.Delete(PendingFile); return; }
                var put = new List<string>(); bool ok = true;
                foreach (var l in File.ReadAllLines(PendingFile))
                {
                    var p = l.Split('\t'); uint v;
                    if (p.Length == 2 && KnownAlias(p[0]) && uint.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) { ok &= Write(p[0], v); put.Add(p[0]); }
                }
                Activate();
                if (ok) File.Delete(PendingFile);
                Log.Say(ok ? "ArchTweaker closed while the power tuner tested {0}. ArchTweaker put the value back." : "ArchTweaker closed while the power tuner tested {0}, and could not put the value back. Results → Put back everything the tuners changed restores the original.", string.Join(", ", put.ToArray()));
            }
            catch (Exception ex) { Log.W("Power tuner recovery: {0}", ex.Message); }
        }

        public static int RestoreAll() { string e; return RestoreAll(out e); }
        // Puts back every saved value. Lines that did not go back stay in the file, so Put back can try again.
        public static int RestoreAll(out string errors)
        {
            int n = 0; var errs = new List<string>(); var keep = new List<string>();
            try
            {
                if (!BackupTrusted()) { errors = File.Exists(BackupFile) ? L.T("an account that is not an administrator owns the backup file") : null; return 0; }
                foreach (var l in File.ReadAllLines(BackupFile))
                {
                    var p = l.Split('\t'); uint v;
                    if (p.Length != 2 || !KnownAlias(p[0]) || !uint.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) continue;
                    if (Write(p[0], v)) n++; else { errs.Add(p[0]); keep.Add(l); }
                }
                Activate();
                if (keep.Count == 0) File.Delete(BackupFile); else File.WriteAllLines(BackupFile, keep.ToArray());
            }
            catch (Exception ex) { errs.Add(ex.Message); }
            errors = errs.Count == 0 ? null : L.F("could not put back: {0}", string.Join(", ", errs.ToArray()));
            Log.W("Power tuner: {0} original value(s) put back{1}", n, errors == null ? "." : " (" + errors + ")");
            return n;
        }

        public static string LastReport;
        public static string WriteReport(List<PowerTrial> res, string game, int secs, int rounds, bool cancelled, string error)
        {
            var when = DateTime.Now;
            var sb = new System.Text.StringBuilder();
            sb.Append(ReportHtml.Head("ArchTweaker power tuner " + when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
            sb.Append("<h1>Power tuner</h1><div class=mut>" + ReportHtml.Esc(when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)) + " · ArchTweaker " + ReportHtml.Esc(Program.Version)
                + " · " + (game != null ? ReportHtml.Esc(game) + " (1 % low FPS, higher is better)" : "timer wake-up delay in µs, lower is better") + " · " + rounds + " × " + secs + " s per side</div>");
            sb.Append("<p>" + res.Count(t => t.Kept) + " value(s) kept." + (cancelled ? " <span class=bad>You stopped the run before the end.</span>" : "") + (error != null ? " <span class=bad>" + ReportHtml.Esc(error) + "</span>" : "") + "</p>");
            sb.Append("<table><tr><th>Setting</th><th>Value tried</th><th class=n>Current</th><th class=n>New</th><th class=n>Change</th><th>Result</th></tr>");
            foreach (var t in res)
            {
                bool done = t.Base.Count >= 2 && t.Try.Count >= 2;
                sb.Append("<tr><td>" + ReportHtml.Esc(t.Step) + "</td><td>" + ReportHtml.Esc(t.Value) + "</td>"
                    + (done ? "<td class=n>" + t.MeanBase.ToString("0.00", CultureInfo.InvariantCulture) + "</td><td class=n>" + t.MeanTry.ToString("0.00", CultureInfo.InvariantCulture) + "</td><td class=n>" + t.DiffPct.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + " %</td>" : "<td class=n>—</td><td class=n>—</td><td class=n>—</td>")
                    + "<td class=" + (t.Kept ? "ok" : t.Note == "made it worse" ? "bad" : "mut") + ">" + ReportHtml.Esc(t.Note) + "</td></tr>");
            }
            sb.Append("</table><p class=mut>Results → Put back everything the tuners changed restores the values from before the first run.</p></body></html>");
            string file = Path.Combine(Bench.ReportDir, "power_" + when.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".html");
            File.WriteAllText(file, sb.ToString(), new System.Text.UTF8Encoding(false));
            return file;
        }

        static string Join(uint[] v) { return string.Join(" / ", v.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()); }

        public static List<PowerTrial> Run(string game, int secs, int rounds, Action<string> say, out string error)
        {
            Cancel = false; Tuner.Cancel = false; error = null;
            bool higher = game != null;
            var res = new List<PowerTrial>();
            var steps = Steps();
            Log.W("Power tuner: {0} setting(s), {1} round(s) × {2} s, metric {3}", steps.Count, rounds, secs, higher ? "1 % low FPS of " + game : "timer wake-up delay");
            for (int s = 0; s < steps.Count && !Cancel && error == null; s++)
            {
                var st = steps[s];
                bool noAnswer = false;
                var cur = st.Aliases.Select(a => { bool na; var v = Read(a, out na); noAnswer |= na; return v; }).ToArray();
                if (cur.Any(x => !x.HasValue))
                {
                    string why = noAnswer ? "powercfg did not answer" : "not on this plan";
                    res.Add(new PowerTrial { Step = st.Name, Note = why }); Log.W("Power tuner: {0}: {1}, skipped.", st.Name, why); continue;
                }
                var current = cur.Select(x => x.Value).ToArray();
                bool saved = true;
                for (int i = 0; i < st.Aliases.Length; i++) saved &= SaveOriginal(st.Aliases[i], current[i]);
                if (!saved) { error = L.T("Could not save the original values, so nothing was changed."); break; }
                foreach (var cand in st.Values)
                {
                    if (Cancel || error != null) break;
                    if (cand.SequenceEqual(current)) continue;
                    var tr = new PowerTrial { Step = st.Name, Value = Join(cand) + "  (" + L.T("was") + " " + Join(current) + ")" };
                    res.Add(tr);
                    Pending(st.Aliases, current);   // 3.27.1: if ArchTweaker closes mid-trial, the next start puts 'current' back
                    try
                    {
                        for (int r = 1; r <= rounds && error == null && !Cancel; r++)
                            foreach (bool on in r % 2 == 1 ? new[] { false, true } : new[] { true, false })
                            {
                                if (Cancel) break;
                                say(L.F("Setting {0} of {1}: {2}. Round {3} of {4}, {5}.", s + 1, steps.Count, L.T(st.Name), r, rounds, on ? L.T("new value") : L.T("current value")));
                                if (!Set(st.Aliases, on ? cand : current)) { tr.Note = "could not set the value"; error = L.F("powercfg did not accept {0}.", L.T(st.Name)); break; }
                                string err; double v = Tuner.Measure(game, secs, out err);
                                if (err != null || v < 0) { error = err ?? "no measurement"; break; }
                                (on ? tr.Try : tr.Base).Add(v);
                            }
                    }
                    catch (Exception ex) { error = ex.Message; }
                    finally
                    {
                        if (!Set(st.Aliases, current)) { error = L.F("{0} did not go back. Results → Put back everything the tuners changed restores it.", L.T(st.Name)); Log.Say("Power tuner: {0}", error); }
                        else Pending(null, null);
                    }
                    if (tr.Base.Count < 2 || tr.Try.Count < 2) { tr.Note = Cancel ? "stopped" : "not finished"; Log.W("Power tuner: {0} = {1}: {2}, value put back.", st.Name, tr.Value, Cancel ? "stopped" : "not finished"); continue; }
                    Decide(tr, higher);
                    if (tr.Kept) { if (Set(st.Aliases, cand)) current = cand; else { tr.Kept = false; tr.Note = "could not set the value"; } }
                    Log.W("Power tuner: {0} = {1}: {2:0.00} → {3:0.00} ({4:+0.0;-0.0} %) → {5}", st.Name, tr.Value, tr.MeanBase, tr.MeanTry, tr.DiffPct, tr.Kept ? "kept" : "back");
                }
            }
            try { LastReport = WriteReport(res, game, secs, rounds, Cancel, error); } catch (Exception ex) { LastReport = null; Log.Say("Could not save the report: {0}", ex.Message); }
            if (Cancel) Log.W("Power tuner: stopped. The current values stay.");
            if (error != null) Log.W("Power tuner: stopped by an error: {0}", error);
            return res;
        }
    }

    public class PowerTunerPage : Panel, IPage
    {
        readonly ComboBox game = Theme.Drop(240, "Game", 8);
        readonly ComboBox secs = Theme.Drop(70, "Seconds per sample");
        readonly ComboBox rounds = Theme.Drop(60, "Rounds");
        readonly ListView lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
        readonly Button runBtn, stopBtn, backBtn;
        readonly Label status;
        bool running;


        public PowerTunerPage()
        {
            BackColor = Theme.Bg;
            Theme.DarkList(lv);
            lv.Columns.Add("Setting", 300); lv.Columns.Add("Value tried", 200); lv.Columns.Add("Current", 100); lv.Columns.Add("New", 100); lv.Columns.Add("Change", 90); lv.Columns.Add("Result", 160);
            var bar = Theme.Bar();
            bar.Controls.Add(Theme.BarLabel("Game:")); bar.Controls.Add(game);
            bar.Controls.Add(Theme.BarLabel("Seconds per sample:"));
            secs.Items.AddRange(new object[] { "10 s", "20 s", "30 s" }); secs.SelectedIndex = 1; bar.Controls.Add(secs);
            bar.Controls.Add(Theme.BarLabel("Rounds:"));
            rounds.Items.AddRange(new object[] { "2", "3" }); rounds.SelectedIndex = 0; bar.Controls.Add(rounds);
            var bar2 = Theme.Bar();
            runBtn = Theme.Lead("Start tuning", delegate { Run(); }); bar2.Controls.Add(runBtn);
            stopBtn = Theme.Btn("Stop", delegate { PowerTuner.Cancel = true; Tuner.Cancel = true; status.Text = L.T("Stopping after this sample…"); }); stopBtn.Enabled = false; bar2.Controls.Add(stopBtn);
            backBtn = Theme.Btn("Put back the original values", delegate { Back(); }); bar2.Controls.Add(backBtn);
            status = Theme.StatusText();
            bar2.Controls.Add(status);
            Controls.Add(lv);
            Controls.Add(Theme.Note("Choose a running game to measure its 1 % low FPS. Choose \"No game\" to measure the timer wake-up delay."));
            Controls.Add(Theme.Note("ArchTweaker tries values of four processor power settings on the active power plan. It keeps a value only when the value measurably helps. The others go back."));
            Controls.Add(bar2); Controls.Add(bar);
            Skin.SetEmpty(lv, "No results yet. Click Start tuning.");
        }

        public void OnShow()
        {
            backBtn.Enabled = !running && PowerTuner.HasBackup;
            if (running) return;
            Bench.FillGameBox(game, "No game: timer wake-up delay");
        }
        public void Filter(string q) { }

        void Run()
        {
            if (running) return;
            string g = game.SelectedIndex > 0 ? game.SelectedItem as string : null;
            if (g != null && !PresentMon.Available) { Dlg.Error("PresentMon.exe not found", "Choose PresentMon.exe on the Benchmark page, or choose \"No game\"."); return; }
            int s = secs.SelectedIndex == 0 ? 10 : secs.SelectedIndex == 2 ? 30 : 20, r = rounds.SelectedIndex + 2;
            running = true; runBtn.Enabled = false; stopBtn.Enabled = true; backBtn.Enabled = false;
            List<PowerTrial> res = null; string err = null;
            Action<string> say = Theme.SayTo(this, status);
            MainForm.Instance.Busy("Tuning power settings…", delegate { res = PowerTuner.Run(g, s, r, say, out err); }, delegate
            {
                running = false; runBtn.Enabled = true; stopBtn.Enabled = false; backBtn.Enabled = PowerTuner.HasBackup; status.Text = "";
                lv.BeginUpdate(); lv.Items.Clear();
                foreach (var t in res ?? new List<PowerTrial>())
                {
                    bool done = t.Base.Count >= 2 && t.Try.Count >= 2;
                    var it = new ListViewItem(new[] { L.T(t.Step), t.Value, done ? t.MeanBase.ToString("0.00") : "—", done ? t.MeanTry.ToString("0.00") : "—", done ? t.DiffPct.ToString("+0.0;-0.0") + " %" : "—", L.T(t.Note) });
                    if (t.Kept) it.ForeColor = Theme.Ok;
                    lv.Items.Add(it);
                }
                lv.EndUpdate(); Skin.Loaded(lv, "No results.");
                if (err != null) Log.Say("Power tuner: {0}", err);
                Log.Say("Power tuner done: {0} value(s) kept.", (res ?? new List<PowerTrial>()).Count(t => t.Kept));
            });
        }

        void Back()
        {
            int n = 0; string errs = null;
            MainForm.Instance.Busy("Putting back the original values…", () => { n = PowerTuner.RestoreAll(out errs); }, () =>
            {
                backBtn.Enabled = PowerTuner.HasBackup;
                if (errs == null) Log.Say("Power tuner: {0} original value(s) put back.", n);
                else Dlg.Error("Not every value went back", L.F("{0} original value(s) put back; {1}. Click Put back again to retry.", n, errs));
            });
        }
    }
}
