// 3.43: Stutter finder page. Pick the running game, click Find stutters and go back to the game. Afterwards each slow
// frame is listed with what happened at that moment (a driver delay, a program starting, a busy disk or CPU, or
// nothing outside the game), plus what to do about the main cause. The engine is StutterFinder.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ArchTweaker
{
    public class StutterPage : Panel, IPage
    {
        readonly ComboBox game = Theme.Drop(240, "Game", 8);
        readonly ComboBox secs = Theme.Drop(70, "Recording length");
        readonly ListView lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
        readonly Label status, summary, advice;
        readonly FlowLayoutPanel actions;
        readonly Button runBtn, copyBtn;
        StutterFinder.Report last;
        bool running;
        const string NoGame = "Choose the running game";

        public StutterPage()
        {
            BackColor = Theme.Bg;
            Theme.DarkList(lv);
            lv.Columns.Add("Time", 70); lv.Columns.Add("Frame", 80); lv.Columns.Add("Cause", 120); lv.Columns.Add("What happened", 640);
            lv.ShowItemToolTips = true;

            var bar = Theme.Bar();
            bar.Controls.Add(Theme.BarLabel("Game:")); bar.Controls.Add(game);
            bar.Controls.Add(Theme.Btn("Refresh", delegate { FillGames(); }));
            bar.Controls.Add(Theme.BarLabel("Record:")); secs.Items.AddRange(new object[] { "30 s", "60 s", "120 s" }); secs.SelectedIndex = 1; bar.Controls.Add(secs);
            runBtn = Theme.Lead("Find stutters", delegate { Run(); });
            bar.Controls.Add(runBtn);
            copyBtn = Theme.Btn("Copy report", delegate { Copy(); }); copyBtn.Enabled = false;
            bar.Controls.Add(copyBtn);
            status = Theme.StatusText();
            bar.Controls.Add(status);

            summary = Theme.GrowToFit(new Label { Dock = DockStyle.Top, Height = 26, Font = Theme.FB, ForeColor = Theme.Text, UseMnemonic = false, Padding = new Padding(2, 6, 2, 0) });
            advice = Theme.GrowToFit(new Label { Dock = DockStyle.Top, Height = 22, Font = Theme.F, ForeColor = Theme.Muted, UseMnemonic = false, Padding = new Padding(2, 4, 2, 0) });
            actions = Theme.Bar(); actions.Visible = false;

            Controls.Add(lv); Controls.Add(actions); Controls.Add(advice); Controls.Add(summary);
            Controls.Add(Theme.Note("Start the game, pick it here, click Find stutters and go back to the game. ArchTweaker records every frame, every long driver delay (DPC/ISR), every program that starts, and the CPU and disk load, all on one clock. Then it shows each slow frame with what happened at that moment. Needs PresentMon (see the Benchmark page)."));
            Controls.Add(bar);
            Skin.SetEmpty(lv, "No recording yet. Start the game, then click Find stutters.");
        }

        public void OnShow() { FillGames(); }
        public void Filter(string q) { }

        void FillGames()
        {
            string keep = game.SelectedItem as string;
            if (keep == null) try { keep = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "BenchGame") as string; } catch { }   // the Benchmark page's game
            game.Items.Clear(); game.Items.Add(L.T(NoGame));
            foreach (var g in Bench.Games()) game.Items.Add(g);
            int i = keep == null ? -1 : game.Items.IndexOf(keep);
            game.SelectedIndex = i >= 0 ? i : game.Items.Count > 1 ? 1 : 0;
        }

        void Run()
        {
            if (running) return;
            string g = game.SelectedIndex > 0 ? game.SelectedItem as string : null;
            if (g == null) { Dlg.Error("Choose the game", "Start the game first, click Refresh, then pick it in the list."); return; }
            if (!PresentMon.Available) { Dlg.Error("PresentMon.exe not found", "Open the Benchmark page and click Choose PresentMon.exe… to add it."); return; }
            int s = secs.SelectedIndex == 0 ? 30 : secs.SelectedIndex == 2 ? 120 : 60;
            running = true; runBtn.Enabled = false;
            StutterFinder.Report res = null;
            Action<string> say = Theme.SayTo(this, status);
            AppShell.Current.Busy(L.F("Recording {0} for {1} s…", g, s), delegate { res = StutterFinder.Run(g, s, say); }, delegate
            {
                running = false; runBtn.Enabled = true; status.Text = "";
                ShowReport(res);
            });
        }

        void ShowReport(StutterFinder.Report r)
        {
            last = r;
            summary.Text = r == null ? L.T("The recording stopped. The log has the details.") : StutterFinder.Summary(r);
            summary.ForeColor = r == null || r.Error != null ? Theme.Warn : Theme.Text;
            string a = r == null ? null : StutterFinder.Advice(r);
            if (r != null && r.TraceError != null) a = (a == null ? "" : a + " ") + L.T("The driver trace didn't run:") + " " + L.T(r.TraceError);
            advice.Text = a ?? ""; advice.Visible = a != null;
            ShowActions(r);
            lv.BeginUpdate(); lv.Items.Clear();
            if (r != null)
                foreach (var s in r.Stutters)
                {
                    var it = new ListViewItem(new[] { Clock(s.AtSec), s.Ms.ToString("0.0", CultureInfo.CurrentCulture) + " ms", CauseName(s.Kind), s.Text }) { Tag = s, ToolTipText = s.Text };
                    it.ForeColor = s.Kind == StutterFinder.Cause.Game ? Theme.Muted : Theme.Text;
                    lv.Items.Add(it);
                }
            lv.EndUpdate();
            Skin.Loaded(lv, r != null && r.Error == null && r.Frames >= 10 ? "No slow frames in this recording." : "No recording yet. Start the game, then click Find stutters.");
            copyBtn.Enabled = r != null && r.Error == null && r.Frames >= 10;
        }

        // Buttons for the pages that deal with the main cause
        void ShowActions(StutterFinder.Report r)
        {
            actions.Controls.Clear();
            var pages = new List<string>();
            if (r != null && r.Stutters.Count > 0)
                switch (r.Main)
                {
                    case StutterFinder.Cause.Driver: pages.AddRange(new[] { "Latency", "Interrupt tuner" }); break;
                    case StutterFinder.Cause.Program: pages.AddRange(new[] { "Startup", "Scheduled tasks" }); break;
                    case StutterFinder.Cause.Disk: pages.AddRange(new[] { "Services", "Disk space" }); break;
                    case StutterFinder.Cause.Cpu: pages.Add("Startup"); break;
                }
            foreach (var p in pages)
            {
                string page = p;
                actions.Controls.Add(Theme.Btn(L.F("Open {0}", L.T(page)), delegate { AppShell.Current.ShowPage(page); }));
            }
            actions.Visible = actions.Controls.Count > 0;
        }

        static string Clock(double sec) { int s = (int)sec; return (s / 60) + ":" + (s % 60).ToString("00") + "." + ((int)((sec - s) * 10)).ToString(CultureInfo.InvariantCulture); }

        public static string CauseName(StutterFinder.Cause c)
        {
            switch (c)
            {
                case StutterFinder.Cause.Driver: return L.T("Driver delay");
                case StutterFinder.Cause.Program: return L.T("Program started");
                case StutterFinder.Cause.Disk: return L.T("Disk busy");
                case StutterFinder.Cause.Cpu: return L.T("CPU busy");
                default: return L.T("The game");
            }
        }

        void Copy()
        {
            var r = last; if (r == null) return;
            var sb = new StringBuilder();
            sb.AppendLine("ArchTweaker " + Program.Version + " · " + L.T("Stutter finder") + " · " + r.Game + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine(StutterFinder.Summary(r));
            string a = StutterFinder.Advice(r); if (a != null) sb.AppendLine(a);
            sb.AppendLine();
            foreach (var s in r.Stutters) sb.AppendLine(Clock(s.AtSec) + "\t" + s.Ms.ToString("0.0", CultureInfo.InvariantCulture) + " ms\t" + CauseName(s.Kind) + "\t" + s.Text);
            try { Clipboard.SetText(sb.ToString()); AppShell.Current.Notify("Report copied.", Toast.Kind.Ok); }
            catch (Exception ex) { Log.W("Stutter finder: copy: {0}", ex.Message); AppShell.Current.Notify("Couldn't copy the report.", Toast.Kind.Warn); }
        }
    }
}
