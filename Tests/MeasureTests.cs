// Tests for src/Measure/.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ArchTweaker;

static class MeasureTests
{
    [Test] public static void ParseCsv_ComputesFpsAndLows()
    {
        string csv = System.IO.Path.GetTempFileName();
        System.IO.File.WriteAllText(csv, "Application,msBetweenPresents\n" + string.Concat(Enumerable.Repeat("g,6.94\n", 100)) + "g,33.0\n");
        var fs = PresentMon.Parse(System.IO.File.ReadAllLines(csv));
        try { System.IO.File.Delete(csv); } catch { }
        T.True(fs != null && fs.Frames == 101, "101 frames parsed");
        T.True(fs.AvgFps > 120 && fs.AvgFps < 160, "avg FPS in the expected band");
        T.True(fs.P1LowFps < fs.AvgFps, "1% low is below average (the 33ms spike pulls it down)");
    }

    [Test] public static void ABCompare_FlagsClearGain_AndCallsNoiseNoise()
    {
        var gain = ABTest.Compare(new[] { 200.0, 202, 199, 201 }, new[] { 220.0, 221, 219, 222 });
        T.True(gain.Significant && gain.DiffPct > 9 && gain.Verdict.Contains("better"), "clear gain: " + gain.Verdict);
        var noise = ABTest.Compare(new[] { 200.0, 210, 190, 205 }, new[] { 203.0, 195, 208, 199 });
        T.False(noise.Significant, "overlapping runs are noise: " + noise.Verdict);
        T.True(ABTest.Compare(new[] { 1.0 }, new[] { 2.0 }).Verdict.Contains("2+"), "one run per side is not enough");
    }

    [Test] public static void PresentMon_NeverABareName()
    {
        string f = PresentMon.Find();
        T.True(f == null || Path.IsPathRooted(f), "full path or nothing");
    }

    [Test] public static void Health_IgnoresDangerZone()
    {
        T.True(Catalog.All.Where(t => t.Dangerous).All(t => !Catalog.All.Where(x => x.Applicable && x.Impact > 0 && !x.Dangerous).Contains(t)), "score set excludes danger");
    }

    [Test] public static void RestartAB_RoundTripsAndJudges()
    {
        var a = new[] { new[] { 1.0, 1.1, 0.9 }, new[] { 0.5, 0.5, 0.6 }, new[] { 1.5, 1.6, 1.5 }, new[] { 2.0, 2.1, 2.0 } };
        var back = RebootAB.De(RebootAB.Ser(a));
        T.True(back.Length == 4 && back[2][1] == 1.6, "saved and read back");
        var b = new[] { new[] { 0.40, 0.41, 0.39 }, new[] { 0.5, 0.5, 0.6 }, new[] { 1.5, 1.6, 1.5 }, new[] { 2.0, 2.1, 2.0 } };
        bool worse, better; RebootAB.Verdict(a, b, out worse, out better);
        T.True(better && !worse, "lower DPC time = better");
        RebootAB.Verdict(b, a, out worse, out better);
        T.True(worse, "higher DPC time = worse");
        T.True(Catalog.All.Where(t => t.Experimental).All(t => !t.Applicable || RebootAB.Testable(t) || t.Dangerous), "experimental tweaks can be restart-tested");
    }

    [Test] public static void V39_RtssLimit_ReadsFramerateSection()
    {
        T.Eq(Advisor.RtssLimit("[OSD]\nLimit=99\n[Framerate]\nLimit=237\nLimitDenominator=1\n"), 237, "limit in [Framerate]");
        T.Eq(Advisor.RtssLimit("[Framerate]\r\nLimit=0\r\n"), 0, "0 = no limit");
        T.Eq(Advisor.RtssLimit("[OSD]\nLimit=60\n"), 0, "other sections ignored");
        T.Eq(Advisor.RtssLimit(null), 0, "no file");
    }

    // 3.13: runs keep their numbers, are read back oldest first, and compare per minute / per second
    [Test] public static void LatencyRuns_RoundTrip()
    {
        string f = System.IO.Path.Combine(Paths.Root, "latency-runs.tsv"), bak = f + ".testbak";
        bool had = System.IO.File.Exists(f); if (had) System.IO.File.Copy(f, bak, true);
        try
        {
            if (System.IO.File.Exists(f)) System.IO.File.Delete(f);
            var res = new List<DriverStat> {
                new DriverStat { Driver = "ndis.sys", Dpcs = 10, DpcMaxUs = 412.5, DpcTotalUs = 3000, Over500 = 0 },
                new DriverStat { Driver = "nvlddmkm.sys", Isrs = 4, IsrMaxUs = 610, IsrTotalUs = 1500, Over500 = 3, Over1000 = 1 } };
            var r = LatencyRuns.From(res, 30); r.Plan = "Test\tPlan";
            LatencyRuns.Save(r);
            LatencyRuns.Save(new LatencyRun { When = DateTime.Now.AddMinutes(1), Secs = 60, DpcUs = 100, Plan = "B" });
            var all = LatencyRuns.All();
            T.Eq(all.Count, 2, "two runs read back");
            T.Eq(all[0].Worst, "nvlddmkm.sys", "worst driver is the one with the highest DPC/ISR");
            T.True(Math.Abs(all[0].WorstUs - 610) < 0.01 && Math.Abs(all[0].BusyMs - 4.5) < 0.01, "worst µs and busy ms kept");
            T.True(Math.Abs(all[0].SpikesPerMin - 6) < 0.01, "3 spikes in 30 s = 6 per minute");
            T.Eq(all[0].Plan, "Test Plan", "a tab in a name can't break the line");
            T.Eq(all[1].Secs, 60, "newest last");
            for (int i = 0; i < 25; i++) LatencyRuns.Save(new LatencyRun { When = DateTime.Now, Secs = 10 });
            T.Eq(LatencyRuns.All().Count, 20, "only the newest 20 kept");
        }
        finally { try { System.IO.File.Delete(f); if (had) System.IO.File.Move(bak, f); } catch { } }
    }

    // 3.14: measure runs keep their numbers; the newest baseline survives the 20-run cap
    [Test] public static void MeasureRuns_BaselineKept()
    {
        string f = System.IO.Path.Combine(Paths.Root, "measure-runs.tsv"), bak = f + ".testbak";
        bool had = System.IO.File.Exists(f); if (had) System.IO.File.Copy(f, bak, true);
        try
        {
            MeasureRuns.Clear();
            T.True(MeasureRuns.Baseline == null, "no baseline at first");
            MeasureRuns.Save(new MetricSet { TimerMs = 15.625, Sleep1Avg = 15.2, Sleep1P99 = 15.9, Sleep1Max = 17, JitterMaxUs = 310 }, true);
            for (int i = 0; i < 30; i++) MeasureRuns.Save(new MetricSet { TimerMs = 0.5, Sleep1Avg = 1.02, JitterMaxUs = 96 }, false);
            var all = MeasureRuns.All();
            T.Eq(all.Count, 20, "only 20 runs kept");
            T.True(all[0].Baseline && Math.Abs(all[0].M.TimerMs - 15.625) < 0.0001, "the baseline is still there, with its numbers");
            T.True(Math.Abs(MeasureRuns.Baseline.JitterMaxUs - 310) < 0.01, "Baseline reads it back");
            T.True(!all[19].Baseline && Math.Abs(all[19].M.Sleep1Avg - 1.02) < 0.001, "newest run last");
        }
        finally { try { System.IO.File.Delete(f); if (had) System.IO.File.Move(bak, f); } catch { } }
    }

    [Test] public static void PresentMon_ParseV1()
    {
        var l = new List<string> { "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,TimeInSeconds,MsBetweenPresents,MsInPresentAPI" };
        for (int i = 0; i < 1000; i++) l.Add("game.exe,100,0x1,DXGI,0,0,1,Hardware: Independent Flip," + (i * 0.004).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + (i == 500 ? "40.0" : "4.0") + ",0.1");
        for (int i = 0; i < 30; i++) l.Add("game.exe,100,0x2,DXGI,0,0,0,Composed: Flip,0,16.6,0.1");   // overlay swap chain: left out
        var f = PresentMon.Parse(l.ToArray());
        T.True(f != null, "v1 CSV parsed"); T.Eq(f.Frames, 1000, "only the main swap chain"); T.Eq(f.Column, "msbetweenpresents", "v1 column");
        T.True(Math.Abs(f.MedianMs - 4.0) < 1e-9, "median 4 ms"); T.Eq(f.Stutters, 1, "one stutter (40 ms > 2.5 × 4)");
        T.True(Math.Abs(f.MaxMs - 40) < 1e-9, "worst frame"); T.True(f.P01LowFps < f.P1LowFps && f.P1LowFps <= f.AvgFps, "0.1 % low ≤ 1 % low ≤ average");
        T.Eq(f.PresentMode, "Hardware: Independent Flip", "present mode"); T.Eq(f.Runtime, "DXGI", "API");
    }

    [Test] public static void PresentMon_ParseV2()
    {
        var l = new List<string> { "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,FrameType,CPUStartTime,FrameTime,CPUBusy,CPUWait,GPULatency,GPUTime,GPUBusy,GPUWait,DisplayLatency,DisplayedTime" };
        for (int i = 0; i < 200; i++) l.Add("game.exe,7,0xA,DXGI,0,0,1,Hardware: Independent Flip,Application," + i + ",5.0,3.0,2.0,0.5,4.0,3.5,1.0," + (i % 10 == 0 ? "NA" : "12.0") + ",5.0");
        var f = PresentMon.Parse(l.ToArray());
        T.True(f != null, "v2 CSV parsed"); T.Eq(f.Column, "frametime", "v2 column FrameTime"); T.Eq(f.Frames, 200, "frames");
        T.True(Math.Abs(f.AvgFps - 200) < 1e-6, "200 FPS"); T.True(Math.Abs(f.CpuBusyMs - 3) < 1e-9, "CPU busy"); T.True(Math.Abs(f.GpuBusyMs - 3.5) < 1e-9, "GPU busy");
        T.True(Math.Abs(f.DisplayLatencyMs - 12) < 1e-9, "display latency skips NA");
        var v3 = PresentMon.Parse(new[] { "Application,MsBetweenAppStart,MsCPUBusy", "g.exe,10,1", "g.exe,10,1", "g.exe,10,1", "g.exe,10,1", "g.exe,10,1" });
        T.True(v3 != null && v3.Column == "msbetweenappstart" && Math.Abs(v3.AvgFps - 100) < 1e-9, "newer column MsBetweenAppStart");
        T.Eq(PresentMon.Parse(new[] { "Application,Nothing", "a,1" }), null, "no frame-time column");
    }

    [Test] public static void Bench_RunsAndReport()
    {
        var r = new BenchRun { When = new DateTime(2026, 11, 12, 17, 30, 0), Game = "game.exe", Report = @"C:\x\bench.html", Plan = "Bal\tanced", WorstDriver = "nvlddmkm.sys",
            TimerMs = 0.5, SleepAvg = 1.02, SleepP99 = 1.1, JitterUs = 96, DpcPct = 0.12, IsrPct = 0.03, WorstDpcUs = 231, WorstIsrUs = 120, AvgFps = 240.5, P1Fps = 180.2, P01Fps = 150.1, P99Ms = 6.1,
            TraceSecs = 30, Spikes = 3, FrameSecs = 60, Frames = 14400, Stutters = 2, Applied = 12 };
        var b = BenchRuns.ParseLine(BenchRuns.Line(r));
        T.True(b != null, "line read back"); T.Eq(b.Game, "game.exe", "game"); T.Eq(b.Frames, 14400, "frames"); T.True(Math.Abs(b.P1Fps - 180.2) < 1e-9, "1 % low");
        T.Eq(b.WorstUs, 231.0, "worst of DPC and ISR"); T.True(b.HasFrames, "has frames"); T.Eq(b.Stutters, 2, "stutters");
        T.Eq(BenchRuns.ParseLine("broken\tline"), null, "damaged line skipped");
        var all = new List<BenchRun> { new BenchRun { Game = "a.exe" }, new BenchRun { Game = "b.exe" }, new BenchRun { Game = "A.EXE" } };
        T.True(BenchRuns.Previous(all, all[2]) == all[0], "previous run of the same game");
        T.Eq(BenchRuns.Previous(all, all[1]), null, "no previous run of b.exe");
        var bk = BenchReport.Buckets(Enumerable.Range(0, 1000).Select(i => i == 437 ? 50.0 : 4.0).ToList(), 100);
        T.Eq(bk.Count, 100, "100 buckets"); T.True(bk.Max() == 50.0, "a stutter survives the down-sampling");
        var fs = PresentMon.Parse(new[] { "Application,MsBetweenPresents", "g,4", "g,4", "g,<b>", "g,4", "g,4", "g,30" });
        var html = BenchReport.Html(new BenchResult { Run = new BenchRun { Game = "<script>.exe", AvgFps = 200, P1Fps = 100, P01Fps = 90, FrameSecs = 60, TraceSecs = 30, Stutters = 1 }, Frames = fs,
            Drivers = new List<DriverStat> { new DriverStat { Driver = "a<b>.sys", DpcMaxUs = 700, Dpcs = 5 } } }, r);
        T.True(html.Contains("&lt;script&gt;.exe") && !html.Contains("<script>.exe"), "game name escaped");
        T.True(html.Contains("a&lt;b&gt;.sys"), "driver name escaped"); T.True(html.Contains("<polyline"), "frame-time chart drawn");
        T.True(html.Contains("was 180.2"), "change against the previous run");
    }

    [Test] public static void MousePoll_Math()
    {
        T.Eq(PollMath.Nominal(998), 1000, "998 Hz is 1000"); T.Eq(PollMath.Nominal(7600), 8000, "8k"); T.Eq(PollMath.Nominal(130), 125, "125");
        double f = 10000000;   // 10 MHz counter
        var st = new List<long>(); long t = 0; var rnd = new Random(3);
        for (int i = 0; i < 2000; i++) { t += 10000 + rnd.Next(-200, 201); if (i == 1000) t += 2000000; if (i == 1500) t += 15000; st.Add(t); }
        var s = PollMath.Analyse(st, f);
        T.True(s != null, "analysed"); T.Eq(s.NominalHz, 1000, "1000 Hz mouse");
        T.True(Math.Abs(s.RateHz - 1000) < 15, "rate near 1000"); T.True(s.LatePct > 0 && s.LatePct < 1, "one late report");
        T.True(s.MaxUs >= 2400 && s.MaxUs < 3000, "the 200 ms stop is left out, the 2.5 ms gap stays");
        T.Eq(PollMath.Analyse(new List<long> { 1, 2, 3 }, f), null, "too few reports");
        T.Eq(PollMath.VidPid(@"\\?\HID#VID_046d&PID_C539&MI_01&Col01#8&1#{378de44c}"), "VID 046D, PID C539", "VID/PID");
        T.Eq(PollMath.VidPid("nothing"), "", "no VID/PID");
        s.When = new DateTime(2026, 11, 13, 12, 0, 0); s.Device = "Mouse (VID 046D, PID C539)";
        var b = PollRuns.ParseLine(PollRuns.Line(s));
        T.True(b != null && b.Device == s.Device && b.NominalHz == 1000 && Math.Abs(b.RateHz - s.RateHz) < 0.1, "run line read back");
    }

    // ---- 3.19
    [Test] public static void LiveMath_FpsAndLow()
    {
        double fps, low, last;
        var ms = Enumerable.Repeat(5.0, 400).ToList(); ms[10] = 50; ms[20] = 50; ms[30] = 50; ms[40] = 50;
        LiveMath.Stats(ms, 1000, out fps, out low, out last);
        T.True(Math.Abs(fps - 200) < 1e-9, "FPS over the last second"); T.True(Math.Abs(low - 20) < 1e-9, "1 % low = slowest 1 % (4 frames of 50 ms)"); T.Eq(last, 5.0, "newest frame");
        LiveMath.Stats(new List<double>(), 1000, out fps, out low, out last);
        T.True(fps < 0 && low < 0 && last < 0, "no frames: -1");
        LiveMath.Stats(Enumerable.Repeat(10.0, 5).ToList(), 1000, out fps, out low, out last);
        T.True(Math.Abs(fps - 100) < 1e-9 && low < 0, "under 20 frames: no 1 % low yet");
    }

    [Test] public static void LiveMath_ColumnsAndLevels()
    {
        int ft, pid;
        LiveMath.Columns("Application,ProcessID,SwapChainAddress,MsBetweenPresents", out ft, out pid); T.Eq(ft, 3, "1.x frame time"); T.Eq(pid, 1, "process id");
        LiveMath.Columns("\"Application\",\"ProcessID\",\"FrameTime\"", out ft, out pid); T.Eq(ft, 2, "2.x frame time, quoted header");
        LiveMath.Columns("a,b", out ft, out pid); T.Eq(ft, -1, "no frame-time column");
        T.Eq(LiveMath.CoreLevel(0.5), 0, "0.5 % fine"); T.Eq(LiveMath.CoreLevel(3), 1, "3 % watch"); T.Eq(LiveMath.CoreLevel(7), 2, "7 % bad");
        T.Eq(LiveMath.FpsLevel(200, 180), 0, "steady"); T.Eq(LiveMath.FpsLevel(200, 120), 1, "1 % low at 60 %"); T.Eq(LiveMath.FpsLevel(200, 80), 2, "1 % low at 40 %");
    }

    [Test] public static void Tuner_Eligible()
    {
        Func<string, Tweak> mk = cat => new Tweak { Id = "x", Category = cat, Title = "x", Impact = 2 }.Dw(Hive.HKLM, @"SOFTWARE\X", "v", 1, null);
        T.Eq(Tuner.Ineligible(mk("Gaming")), null, "gaming registry tweak qualifies");
        T.Eq(Tuner.Ineligible(mk("Privacy")), "not a performance category", "privacy skipped");
        T.Eq(Tuner.Ineligible(mk("Gaming").NeedsReboot()), "needs a restart", "restart skipped");
        T.Eq(Tuner.Ineligible(mk("Gaming").Exp()), "experimental", "experimental skipped");
        var z = mk("Gaming"); z.Impact = 0; T.Eq(Tuner.Ineligible(z), "no expected effect", "no impact skipped");
        T.Eq(Tuner.Ineligible(mk("Gaming").R(Risk.Advanced)), "advanced risk", "advanced skipped");
    }

    [Test] public static void Tuner_Decide()
    {
        var up = new TuneStep(); up.Off.AddRange(new[] { 100.0, 101, 99 }); up.On.AddRange(new[] { 110.0, 111, 109 });
        Tuner.Decide(up, true); T.True(up.Kept, "clear FPS gain kept"); T.Eq(up.Note, "helped", "note");
        var down = new TuneStep(); down.Off.AddRange(new[] { 100.0, 101, 99 }); down.On.AddRange(new[] { 110.0, 111, 109 });
        Tuner.Decide(down, false); T.False(down.Kept, "higher Sleep(1) p99 is worse"); T.Eq(down.Note, "made it worse", "worse note");
        var noise = new TuneStep(); noise.Off.AddRange(new[] { 100.0, 110, 90 }); noise.On.AddRange(new[] { 101.0, 91, 111 });
        Tuner.Decide(noise, true); T.False(noise.Kept, "noise not kept"); T.Eq(noise.Note, "within noise", "noise note");
        var tiny = new TuneStep(); tiny.Off.AddRange(new[] { 100.0, 100.01, 99.99 }); tiny.On.AddRange(new[] { 100.5, 100.51, 100.49 });
        Tuner.Decide(tiny, true); T.False(tiny.Kept, "significant but under 1 % not kept");
    }

    [Test] public static void WakeLatency_P99()
    {
        var l = Enumerable.Range(1, 100).Select(i => (double)i).ToList();
        T.True(WakeLatency.P99(l) >= 98 && WakeLatency.P99(l) <= 100, "99th percentile of 1..100");
        T.True(WakeLatency.P99(new List<double>()) < 0 || WakeLatency.P99(new List<double>()) == 0, "empty list gives no value");
    }

    [Test] public static void BootTimes_ParseAndSplit()
    {
        string e100 = "<Event xmlns='" + TestHelp.Ev + "'><System><EventID>100</EventID><TimeCreated SystemTime='2026-11-10T08:00:00.000Z'/></System><EventData><Data Name='BootTime'>31250</Data><Data Name='MainPathBootTime'>12000</Data><Data Name='BootPostBootTime'>19250</Data></EventData></Event>";
        string e101 = "<Event xmlns='" + TestHelp.Ev + "'><System><EventID>101</EventID><TimeCreated SystemTime='2026-11-10T08:00:30.000Z'/></System><EventData><Data Name='FriendlyName'>Steam</Data><Data Name='TotalTime'>4200</Data></EventData></Event>";
        var parts = BootTimes.Split(e100 + "\r\n" + e101 + "\r\n");
        T.Eq(parts.Count, 2, "two events split");
        var r = BootTimes.Parse(parts[0]);
        T.True(r != null && r.BootMs == 31250 && r.MainPathMs == 12000 && r.PostBootMs == 19250, "boot event fields");
        T.Eq(r.When.ToUniversalTime().Hour, 8, "time read");
        var s = BootTimes.SlowItem(parts[1]);
        T.True(s.HasValue && s.Value.Key == "Steam" && s.Value.Value == 4200, "slow item name and time");
        T.True(BootTimes.Parse("<bad") == null && BootTimes.Split("") .Count == 0, "bad input gives nothing");
    }

    [Test] public static void Results_ReportNames()
    {
        string tool; DateTime when;
        T.True(Results.ParseReportName(@"C:\x\tuning_20261114-203015.html", out tool, out when) && tool == "Tuning assistant" && when.Hour == 20 && when.Second == 15, "tuning report");
        T.True(Results.ParseReportName("power_20261114-010203.html", out tool, out when) && tool == "Power tuner", "power report");
        T.True(Results.ParseReportName("tuneall_20261114-010203.html", out tool, out when) && tool == "Tune everything", "tune everything report");
        T.False(Results.ParseReportName("bench_20261114-010203.html", out tool, out when), "benchmark reports have their own rows");
        T.False(Results.ParseReportName("tuning_garbage.html", out tool, out when), "bad date ignored");
    }

    [Test] public static void TuneAll_Report()
    {
        var r = new TuneAllResult { Secs = 20, Rounds = 2, Power = new List<PowerTrial> { new PowerTrial { Step = "Minimum processor state", Kept = true } } };
        string f = TuneAll.WriteReport(r);
        try { var html = System.IO.File.ReadAllText(f); T.True(html.Contains("1 value(s)") && html.Contains("Tune everything"), "summary report lists kept values"); }
        finally { try { System.IO.File.Delete(f); } catch { } }
        T.True(Mode.ProPage("Tune everything"), "Pro page");
    }

    [Test] public static void BootTimes_QueryErrorIsNotNoTimings()
    {
        T.True(BootTimes.QueryError("") == null && BootTimes.QueryError("<Event xmlns='x'/>") == null, "XML or nothing is not an error");
        T.Eq(BootTimes.QueryError("Failed to read events. Access is denied.\r\n"), "Failed to read events. Access is denied.", "wevtutil error text is reported");
        T.True(BootTimes.Parse("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///c:/x'>]><Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>&e;</Event>") == null, "a DTD is refused");
    }

    [Test] public static void Bench_ReportPathStaysInReportDir()
    {
        string ok = System.IO.Path.Combine(Bench.ReportDir, "bench_20261114-101010.html");
        T.Eq(Bench.ReportIn(ok), System.IO.Path.GetFullPath(ok), "a report in the folder");
        T.True(Bench.ReportIn(System.IO.Path.Combine(Bench.ReportDir, "..", "..", "x.html")) == null, "traversal out of the folder refused");
        T.True(Bench.ReportIn(System.IO.Path.Combine(Bench.ReportDir, "run.exe")) == null, "not an .html file refused");
        T.True(Bench.ReportIn("") == null && Bench.ReportIn(null) == null, "empty refused");
    }

    // 3.34
    [Test] public static void Proof_ParseRoundTripsAndGain()
    {
        var p = ProofStore.Parse("gd_msi\t20261116-021500\t1 % low FPS\tFPS\t+3.2\t3\t1\t26100\t560.94\tA/B test");
        T.True(p.HasValue, "parses a 10-field line");
        var v = p.Value;
        T.Eq(v.Id, "gd_msi", "id"); T.Eq(v.Build, 26100, "build"); T.Eq(v.Samples, 3, "samples");
        T.True(v.Significant, "significant flag"); T.True(System.Math.Abs(v.GainPct - 3.2) < 0.001, "gain");
        T.True(v.Helped, "+3.2 % significant is a win"); T.False(v.Worse, "not worse");
        T.True(v.Short().Contains("+3.2"), "short shows the gain");
        T.Eq(ProofStore.Parse("too\tfew\tfields"), (Proof?)null, "short line rejected");
        var worse = ProofStore.Parse("x\t20261116-021500\tm\tu\t-2.0\t4\t1\t0\t\tA/B test").Value;
        T.True(worse.Worse, "-2 % significant is worse"); T.False(worse.Helped, "worse is not a win");
        var noise = ProofStore.Parse("x\t20261116-021500\tm\tu\t+0.4\t4\t0\t0\t\tA/B test").Value;
        T.False(noise.Helped, "within noise is not a win"); T.False(noise.Worse, "within noise is not worse");
    }

    // 3.39: one verdict per measurement, used by the row chip and by Optimize safely
    [Test] public static void Proof_KindOf_GivesOneVerdict()
    {
        var won = ProofStore.Parse("a\t20261116-021500\tm\tu\t+3.0\t3\t1\t0\t\tA/B test").Value;
        var noise = ProofStore.Parse("b\t20261116-021500\tm\tu\t+0.4\t4\t0\t0\t\tA/B test").Value;
        var worse = ProofStore.Parse("c\t20261116-021500\tm\tu\t-2.0\t4\t1\t0\t\tA/B test").Value;
        var thin = ProofStore.Parse("d\t20261116-021500\tm\tu\t+0.2\t2\t0\t0\t\tA/B test").Value;
        T.Eq(ProofStore.KindOf(won, false), ProofKind.Proven, "a real win");
        T.Eq(ProofStore.KindOf(noise, false), ProofKind.NoGain, "within noise, enough runs");
        T.Eq(ProofStore.KindOf(worse, false), ProofKind.Worse, "measured worse");
        T.Eq(ProofStore.KindOf(thin, false), ProofKind.TooFew, "2 runs each is too thin to act on");
        T.Eq(ProofStore.KindOf(won, true), ProofKind.Stale, "stale beats everything");
        T.True(LiteActions.IsNoGain(ProofKind.NoGain) && LiteActions.IsNoGain(ProofKind.Worse), "no gain and worse are offered for undo");
        T.False(LiteActions.IsNoGain(ProofKind.Proven) || LiteActions.IsNoGain(ProofKind.Stale) || LiteActions.IsNoGain(ProofKind.TooFew) || LiteActions.IsNoGain(null), "the rest are not");
    }

    [Test] public static void Proof_StaleOnBuildChange()
    {
        string why;
        var same = ProofStore.Parse("x\t20261116-021500\tm\tu\t+2.0\t3\t1\t" + OSInfo.Build + "\t\tA/B test").Value;
        T.False(same.Stale(out why), "same build, no GPU recorded: not stale");
        var old = ProofStore.Parse("x\t20261116-021500\tm\tu\t+2.0\t3\t1\t1\t\tA/B test").Value;
        if (OSInfo.Build > 1) T.True(old.Stale(out why), "a different build is stale");
    }

    // ---- 3.35
    [Test] public static void Proof_GpuPick_PrefersTheRealGpu_InAFixedOrder()
    {
        var a = new List<KeyValuePair<string, string>> {
            new KeyValuePair<string, string>(@"ROOT\DISPLAY\0000", "1.0.0.1"),                 // virtual (Parsec / Spacedesk)
            new KeyValuePair<string, string>(@"PCI\VEN_8086&DEV_4680\3&11583659&0&10", "31.0.101.5"),
            new KeyValuePair<string, string>(@"PCI\VEN_10DE&DEV_2208\4&1A2B&0&0008", "32.0.15.9649") };
        T.Eq(ProofStore.PickGpu(a), "32.0.15.9649", "NVIDIA first");
        a.Reverse();
        T.Eq(ProofStore.PickGpu(a), "32.0.15.9649", "the same pick whatever order WMI lists them in");
        T.Eq(ProofStore.PickGpu(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(@"ROOT\DISPLAY\0000", "1.0") }), "1.0", "only a virtual adapter: that one");
        T.Eq(ProofStore.PickGpu(new List<KeyValuePair<string, string>>()), "", "no adapter: empty");
    }

    [Test] public static void Proof_Short_HasNoSpaceBeforePercent()
    {
        var p = ProofStore.Parse("x\t20261116-021500\tm\tu\t3.2\t3\t1\t0\t\tA/B test").Value;
        T.True(p.Short().StartsWith("+3.2% "), "chip text reads +3.2%");
    }

    [Test] public static void Proof_ForgetLine_ParsesAsTombstone()
    {
        var p = ProofStore.Parse("x\t20261116-021500\t" + ProofStore.Forgotten + "\t\t0\t0\t0\t0\t\t");
        T.True(p.HasValue && p.Value.Metric == ProofStore.Forgotten, "a forget line parses, with the tombstone metric");
    }

    [Test] public static void Proof_RecordThenForget_RoundTripsThroughTheFile()
    {
        string f = System.IO.Path.GetTempFileName();
        try
        {
            ProofStore.FileOverride = f; ProofStore.ResetCache();
            var tw = new Tweak { Id = "x_roundtrip", Title = "Round trip" };
            ProofStore.Record(tw, "1 % low FPS", "FPS", 3.2, 5, true, "A/B test");
            ProofStore.ResetCache();   // read it back from the file, not the cache
            var p = ProofStore.Get("x_roundtrip");
            T.True(p.HasValue, "a recorded proof is read back from the file");
            if (p.HasValue) { T.Eq(p.Value.Metric, "1 % low FPS", "metric kept"); T.Eq(p.Value.Samples, 5, "runs kept"); T.True(p.Value.Helped, "a significant +3.2% helped"); }
            T.True(ProofStore.Forget("x_roundtrip"), "forget works");
            ProofStore.ResetCache();
            T.False(ProofStore.Get("x_roundtrip").HasValue, "after a forget, the proof is gone after a reload too");
            T.Eq(System.IO.File.ReadAllLines(f).Length, 2, "append-only: the proof line and the forget line");
        }
        finally { ProofStore.FileOverride = null; ProofStore.ResetCache(); try { System.IO.File.Delete(f); } catch { } }
    }

    // 3.35.1: one escaper for every HTML report. Report.cs had its own that left quotes as they were, and its
    // text goes into title='...' attributes, so an apostrophe broke the tooltip.
    [Test] public static void ReportEscape_HandlesQuotesForAttributes()
    {
        string e = ReportHtml.Esc("Tom's <b>\"x\" & y");
        T.False(e.Contains("'") || e.Contains("\"") || e.Contains("<"), "no raw quote or tag left: " + e);
        T.True(e.Contains("&amp;"), "ampersand escaped");
        T.Eq(ReportHtml.Esc(null), "", "null is empty");
    }
}
