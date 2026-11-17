// ArchTweaker: Benchmark page and its compare cards (3.32 split from Benchmark.cs).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ArchTweaker
{
    public class BenchCompare : CompareCards
    {
        public readonly TrendChart Trend = new TrendChart();
        public void Reload(List<BenchRun> all)
        {
            var b = all.LastOrDefault();
            if (b == null) { Set("", false, null); Trend.Set("", "", "0", new List<TrendChart.Point2>()); return; }
            var same = all.Where(r => string.Equals(r.Game, b.Game, StringComparison.OrdinalIgnoreCase)).ToList();
            Trend.Higher = b.HasFrames;
            if (b.HasFrames) Trend.Set(L.F("1 % low FPS per run ({0})", b.Game), "FPS", "0.0", same.Where(r => r.HasFrames).Select(r => new TrendChart.Point2 { When = r.When, V = r.P1Fps }).ToList());
            else Trend.Set("Worst DPC/ISR per run", "µs", "0", same.Select(r => new TrendChart.Point2 { When = r.When, V = r.WorstUs }).ToList());
            var a = BenchRuns.Previous(all, b);
            string nb = b.When.ToString("dd MMM HH:mm") + (b.Game.Length > 0 ? " · " + b.Game : "");
            string h = a == null ? L.T("Newest run:") + " " + nb + ". " + L.T("Run again after you apply tweaks to see the difference.")
                : L.T("Newest run:") + " " + nb + "   ·   " + L.T("previous:") + " " + a.When.ToString("dd MMM HH:mm");
            Func<Func<BenchRun, double>, double?> was = f => a == null ? (double?)null : f(a);
            bool af = a != null && a.HasFrames;
            var cards = new List<Card>();
            if (b.HasFrames)
            {
                cards.Add(new Card { Name = "Average FPS", Unit = "FPS", Fmt = "0", Now = b.AvgFps, Was = af ? was(r => r.AvgFps) : null, Higher = true });
                cards.Add(new Card { Name = "1 % low", Unit = "FPS", Fmt = "0", Now = b.P1Fps, Was = af ? was(r => r.P1Fps) : null, Higher = true });
                cards.Add(new Card { Name = "0.1 % low", Unit = "FPS", Fmt = "0", Now = b.P01Fps, Was = af ? was(r => r.P01Fps) : null, Higher = true });
            }
            cards.Add(new Card { Name = "Worst DPC/ISR", Unit = "µs", Fmt = "0", Now = b.WorstUs, Was = was(r => r.WorstUs) });
            cards.Add(new Card { Name = "Sleep(1) 99th percentile", Unit = "ms", Fmt = "0.00", Now = b.SleepP99, Was = was(r => r.SleepP99) });
            if (!b.HasFrames) cards.Add(new Card { Name = "Scheduling jitter (max)", Unit = "µs", Fmt = "0.0", Now = b.JitterUs, Was = was(r => r.JitterUs) });
            Set(h, false, cards);
        }
    }

    public class BenchmarkPage : Panel, IPage
    {
        readonly ComboBox game = Theme.Drop(240, "Game", 8);
        readonly ComboBox frameSecs = Theme.Drop(70, "Frame capture length");
        readonly ComboBox traceSecs = Theme.Drop(70, "Driver scan length");
        readonly ListView lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
        readonly BenchCompare compare = new BenchCompare();
        readonly Label status, pmState;
        readonly Button runBtn, openBtn, delBtn;
        List<BenchRun> runs = new List<BenchRun>();
        bool running;
        const string NoGame = "No game: system latency only";

        public BenchmarkPage()
        {
            BackColor = Theme.Bg;
            Theme.DarkList(lv);
            lv.Columns.Add("When", 120); lv.Columns.Add("Game", 170); lv.Columns.Add("Average FPS", 95); lv.Columns.Add("1 % low", 80); lv.Columns.Add("0.1 % low", 80);
            lv.Columns.Add("Stutters", 75); lv.Columns.Add("Worst DPC/ISR", 110); lv.Columns.Add("Sleep(1) p99", 100); lv.Columns.Add("Power plan", 180);
            lv.ItemActivate += delegate { Open(Selected()); };

            var bar = Theme.Bar();
            bar.Controls.Add(Theme.BarLabel("Game:")); bar.Controls.Add(game);
            bar.Controls.Add(Theme.Btn("Refresh", delegate { FillGames(); }));
            bar.Controls.Add(Theme.BarLabel("Frames:")); frameSecs.Items.AddRange(new object[] { "30 s", "60 s", "120 s" }); frameSecs.SelectedIndex = 1; bar.Controls.Add(frameSecs);
            bar.Controls.Add(Theme.BarLabel("Drivers:")); traceSecs.Items.AddRange(new object[] { "10 s", "30 s", "60 s" }); traceSecs.SelectedIndex = 1; bar.Controls.Add(traceSecs);

            var bar2 = Theme.Bar();
            runBtn = Theme.Lead("Run benchmark", delegate { Run(); });
            bar2.Controls.Add(runBtn);
            openBtn = Theme.Btn("Open report", delegate { Open(Selected() ?? runs.LastOrDefault()); });
            bar2.Controls.Add(openBtn);
            bar2.Controls.Add(Theme.Btn("Open reports folder", delegate { Shell.OpenVisible("explorer.exe", "\"" + Bench.ReportDir + "\""); }));
            delBtn = Theme.Btn("Delete run", delegate { Delete(); }); delBtn.Enabled = false;
            bar2.Controls.Add(delBtn);
            bar2.Controls.Add(Theme.Btn("Choose PresentMon.exe…", delegate { ChoosePresentMon(); }));
            lv.SelectedIndexChanged += delegate { delBtn.Enabled = Selected() != null; };

            status = Theme.StatusText();
            bar2.Controls.Add(status);
            pmState = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 22, ForeColor = Theme.Muted, Font = Theme.FS, UseMnemonic = false, Tag = "notr", Padding = new Padding(2, 4, 2, 0) };
            var head = new Label { Text = "Saved runs", Font = Theme.FB, ForeColor = Theme.Text, Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false };

            Controls.Add(lv); Controls.Add(head); Controls.Add(compare.Trend); Controls.Add(compare);
            Controls.Add(pmState);
            Controls.Add(Theme.Note("Start the game in a repeatable scene, click Run benchmark and go back to the game. ArchTweaker measures timers, driver DPC/ISR times and frame times, then opens an HTML report. A benchmark changes nothing."));
            Controls.Add(bar2); Controls.Add(bar);
            Skin.SetEmpty(lv, "No benchmark runs yet. Click Run benchmark.");
        }
        BenchRun Selected() { return lv.SelectedItems.Count == 0 ? null : lv.SelectedItems[0].Tag as BenchRun; }

        public void OnShow() { FillGames(); Reload(); ShowPm(); }
        public void Filter(string q) { }

        void ShowPm()
        {
            string d = PresentMon.Describe();
            pmState.Text = d != null ? L.T("Frame capture:") + " " + d
                : L.T("PresentMon not found. Click Choose PresentMon.exe… and pick PresentMon-x.y.z-x64.exe (free from Intel on GitHub).");
            pmState.ForeColor = d != null ? Theme.Muted : Theme.Warn;
        }

        void FillGames()
        {
            string keep = game.SelectedItem as string ?? Remembered();
            game.Items.Clear(); game.Items.Add(L.T(NoGame));
            foreach (var g in Bench.Games()) game.Items.Add(g);
            int i = keep == null ? -1 : game.Items.IndexOf(keep);
            game.SelectedIndex = i >= 0 ? i : 0;
        }
        static string Remembered() { try { object o = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "BenchGame"); return o as string; } catch { return null; } }
        static void Remember(string g) { try { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(@"Software\ArchTweaker")) k.SetValue("BenchGame", g ?? ""); } catch { } }

        void Reload()
        {
            try { runs = BenchRuns.All(); }
            catch (Exception ex) { runs = new List<BenchRun>(); Log.Say("Could not read the saved benchmark runs: {0}", ex.Message); }
            compare.Reload(runs);
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var r in Enumerable.Reverse(runs))
            {
                Func<double, string, string> v = (x, f) => x >= 0 ? x.ToString(f) : "—";
                lv.Items.Add(new ListViewItem(new[] { r.When.ToString("dd MMM HH:mm"), r.Game.Length > 0 ? r.Game : "—",
                    r.HasFrames ? v(r.AvgFps, "0.0") : "—", r.HasFrames ? v(r.P1Fps, "0.0") : "—", r.HasFrames ? v(r.P01Fps, "0.0") : "—", r.HasFrames && r.Stutters >= 0 ? r.Stutters.ToString() : "—",
                    v(r.WorstUs, "0") + " µs", v(r.SleepP99, "0.00") + " ms", r.Plan }) { Tag = r });
            }
            lv.EndUpdate();
            Skin.Loaded(lv, "No benchmark runs yet. Click Run benchmark.");
            openBtn.Enabled = runs.Count > 0; delBtn.Enabled = Selected() != null;
        }

        void Run()
        {
            if (running) return;
            string g = game.SelectedIndex > 0 ? game.SelectedItem as string : null;
            if (g != null && !PresentMon.Available)
            {
                Dlg.Error("PresentMon.exe not found", "Click Choose PresentMon.exe… and pick the PresentMon console program, or choose \"No game\" to measure system latency only.");
                return;
            }
            Remember(g);
            int fs = frameSecs.SelectedIndex == 0 ? 30 : frameSecs.SelectedIndex == 2 ? 120 : 60;
            int ts = traceSecs.SelectedIndex == 0 ? 10 : traceSecs.SelectedIndex == 2 ? 60 : 30;
            running = true; runBtn.Enabled = false;
            BenchResult res = null;
            Action<string> step = Theme.SayTo(this, status);
            MainForm.Instance.Busy("Running the benchmark…", delegate { res = Bench.Run(g, ts, fs, step); }, delegate
            {
                running = false; runBtn.Enabled = true; status.Text = "";
                Reload();
                if (res == null) { Dlg.Error("The benchmark stopped", "Details are in the log: " + Paths.LogFile); return; }
                if (res.TraceError != null) Log.Say("Driver trace: {0}", res.TraceError);
                if (res.FrameError != null) Log.Say("PresentMon: {0}", res.FrameError);
                if (res.Run.HasFrames) Log.Say("Benchmark done: {0} FPS average, {1} FPS 1 % low, worst DPC/ISR {2} µs.", res.Run.AvgFps.ToString("0.0"), res.Run.P1Fps.ToString("0.0"), res.Run.WorstUs.ToString("0"));
                else Log.Say("Benchmark done: worst DPC/ISR {0} µs.", res.Run.WorstUs.ToString("0"));
                Open(res.Run);
            });
        }

        static void Open(BenchRun r)
        {
            if (r == null) return;
            if (string.IsNullOrEmpty(r.Report) || !File.Exists(r.Report)) { Dlg.Error("Report not found", "This run has no saved report file."); return; }
            Shell.OpenVisible("explorer.exe", "\"" + r.Report + "\"");
        }

        void Delete()
        {
            var r = Selected(); if (r == null) return;
            if (!Dlg.Confirm("Delete this run?", L.F("ArchTweaker deletes the run of {0} and its report.", r.When.ToString("dd MMM HH:mm")), "Delete run", destructive: true)) return;
            BenchRuns.Remove(r); Reload();
        }

        void ChoosePresentMon()
        {
            using (var d = new OpenFileDialog { Filter = "PresentMon|PresentMon*.exe|Programs|*.exe", Title = L.T("Choose PresentMon.exe") })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string err = PresentMon.Import(d.FileName);
                if (err != null) Dlg.Error("Could not use this file", err);
                else Log.Say("PresentMon is ready: {0}", PresentMon.Describe() ?? PresentMon.BinPath);
                ShowPm();
            }
        }
    }
}
