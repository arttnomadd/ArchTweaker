// Tests for src/Network/.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ArchTweaker;

static class NetworkTests
{
    [Test] public static void NetTest_ComputesAverageJitterAndLoss()
    {
        var s = NetTest.Compute("x", new double?[] { 10, 12, null, 11, 15 });
        T.Eq(s.Sent, 5, "sent"); T.Eq(s.Lost, 1, "lost");
        T.True(Math.Abs(s.Avg - 12) < 0.001, "avg 12");
        T.True(Math.Abs(s.Jitter - (2 + 1 + 4) / 3.0) < 0.001, "jitter = mean consecutive change");
        T.True(Math.Abs(s.LossPct - 20) < 0.001, "20 % loss");
        T.Eq(NetTest.Compute("y", new double?[] { null, null }).Line(), "y: no reply", "all lost");
    }

    [Test] public static void NetTest_VerdictPointsAtTheRightHop()
    {
        var goodLan = NetTest.Compute("r", Enumerable.Repeat((double?)1.0, 20).ToList());
        var badLan = NetTest.Compute("r", new double?[] { 1, 9, 1, 9, 1 });
        var goodNet = NetTest.Compute("n", Enumerable.Repeat((double?)20.0, 20).ToList());
        var lossyNet = NetTest.Compute("n", new double?[] { 20, null, 20, 20, 20 });
        T.True(NetTest.Verdict(badLan, goodNet).Contains("router"), "LAN problem");
        T.True(NetTest.Verdict(goodLan, lossyNet).Contains("past the router"), "ISP problem");
        T.True(NetTest.Verdict(goodLan, goodNet).StartsWith("GOOD"), "good");
        T.True(NetTest.Verdict(null, goodNet).StartsWith("GOOD"), "no router found still judges the internet");
    }

    [Test] public static void FastOpen_ReadFromNetsh()
    {
        T.Eq(NetshGlobal.ParseFastOpen("Querying active state...\r\n\r\nReceive-Side Scaling State          : enabled\r\nFast Open                           : enabled\r\nFast Open Fallback                  : disabled\r\n"), (bool?)true, "enabled");
        T.Eq(NetshGlobal.ParseFastOpen("Fast Open Fallback : enabled\r\nFast Open : disabled\r\n"), (bool?)false, "fallback line is not Fast Open");
        T.Eq(NetshGlobal.ParseFastOpen("Быстрое открытие : включено\r\n"), (bool?)null, "unknown output → remembered state");
        T.True(Catalog.All.First(x => x.Id == "cmd_fastopen").Detect != null, "cmd_fastopen reads real state");
    }

    [Test] public static void V39_NetDpcVerdict()
    {
        T.True(NetDpcCheck.Verdict(new[] { new DriverStat { Driver = "ndis.sys", DpcMaxUs = 120 }, new DriverStat { Driver = "nvlddmkm.sys", DpcMaxUs = 900 } }) == null, "GPU spikes are not a network problem");
        string m = NetDpcCheck.Verdict(new[] { new DriverStat { Driver = "e2f68.sys", DpcMaxUs = 640 }, new DriverStat { Driver = "ndis.sys", IsrMaxUs = 510 } });
        T.True(m != null && m.Contains("e2f68.sys") && m.Contains("640"), "worst network driver named");
    }

    // 3.30
    [Test] public static void Bufferbloat_GradesExtraLatency()
    {
        T.Eq(Bufferbloat.Grade(0), "A+", "0 ms"); T.Eq(Bufferbloat.Grade(4.9), "A+", "<5");
        T.Eq(Bufferbloat.Grade(5), "A", "5"); T.Eq(Bufferbloat.Grade(29), "A", "29");
        T.Eq(Bufferbloat.Grade(30), "B", "30"); T.Eq(Bufferbloat.Grade(60), "C", "60");
        T.Eq(Bufferbloat.Grade(200), "D", "200"); T.Eq(Bufferbloat.Grade(400), "F", "400");
        var p = new Bufferbloat.Phase { Name = "At rest" };
        p.Rtt.AddRange(new double[] { 10, 20, 30, 40, 50 });
        T.True(Math.Abs(p.Median - 30) < 0.001, "median"); T.True(Math.Abs(p.Pct(25) - 20) < 0.001, "25 %");
        T.True(Math.Abs(p.Pct(95) - 48) < 0.001, "95 % interpolates");
        var idle = new Bufferbloat.Phase(); idle.Rtt.Add(10);
        var dl = new Bufferbloat.Phase(); dl.Rtt.Add(80);
        var ul = new Bufferbloat.Phase(); ul.Rtt.Add(12);
        var r = new Bufferbloat.Result { Idle = idle, Download = dl, Upload = ul };
        T.Eq(r.Grade, "C", "the worse direction sets the grade");
        T.True(Bufferbloat.Advice("C").Contains("SQM"), "bad grade points at the router");
    }

    // 3.38.3: lost pings under load were left out, so a line where every ping timed out graded A+
    [Test] public static void Bufferbloat_LostPingsUnderLoad_CountAsTimeouts()
    {
        Func<double[], int, Bufferbloat.Phase> ph = (rtt, lost) => { var p = new Bufferbloat.Phase(); p.Rtt.AddRange(rtt); p.Lost = lost; return p; };
        var allLost = new Bufferbloat.Result { Idle = ph(new double[] { 10, 11, 12 }, 0), Download = ph(new double[0], 40), Upload = ph(new double[] { 12 }, 0) };
        T.Eq(allLost.Grade, "F", "every ping lost while downloading is F, not A+");
        var mostLost = new Bufferbloat.Result { Idle = ph(new double[] { 10 }, 0), Download = ph(new double[] { 15, 16 }, 10), Upload = ph(new double[] { 12 }, 0) };
        T.Eq(mostLost.Grade, "F", "most pings lost under load is F");
        var fewLost = new Bufferbloat.Result { Idle = ph(new double[] { 10 }, 0), Download = ph(new double[] { 12, 13, 14, 15, 16, 17, 18, 19, 20 }, 1), Upload = ph(new double[] { 12 }, 0) };
        T.Eq(fewLost.Grade, "A", "one lost ping in ten barely moves the median");
        T.Eq(ph(new double[] { 10, 30 }, 0).LoadedMedian, 20.0, "no loss: same as the median");
        T.Eq(ph(new double[0], 0).LoadedMedian, 0.0, "no samples: 0");
        var noIdle = new Bufferbloat.Result { Idle = ph(new double[0], 50), Download = ph(new double[] { 80 }, 0), Upload = ph(new double[] { 80 }, 0) };
        T.Eq(noIdle.Grade, "?", "no reply at rest: no grade");
        T.True(Bufferbloat.Advice("?").Contains("ping"), "the advice says ping got no reply");
    }

    // 3.38.3: a router that answers no ping was reported as an unstable link
    [Test] public static void NetTest_RouterThatIgnoresPing_IsNotBlamed()
    {
        var silent = NetTest.Compute("r", new double?[] { null, null, null, null });
        var goodNet = NetTest.Compute("n", Enumerable.Repeat((double?)20.0, 20).ToList());
        var lossyNet = NetTest.Compute("n", new double?[] { 20, null, 20, 20, 20 });
        T.True(NetTest.Verdict(silent, goodNet).StartsWith("GOOD"), "silent router, good internet: GOOD");
        T.True(NetTest.Verdict(silent, lossyNet).Contains("past the router"), "silent router, lossy internet: the internet is judged");
        var lossyLan = NetTest.Compute("r", new double?[] { 1, null, 1, 1 });
        T.True(NetTest.Verdict(lossyLan, goodNet).Contains("router"), "a router that answers some pings is still judged");
    }

    [Test] public static void FastOpen_OddOutput_IsUnknown()
    {
        T.Eq(NetshGlobal.ParseFastOpen(null), (bool?)null, "null output");
        T.Eq(NetshGlobal.ParseFastOpen(""), (bool?)null, "empty output");
        T.Eq(NetshGlobal.ParseFastOpen("Fast Open : default\r\n"), (bool?)null, "a value that is not enabled/disabled");
        T.Eq(NetshGlobal.ParseFastOpen("fast open: DISABLED"), (bool?)false, "case and spacing do not matter");
    }

    [Test] public static void NetDpc_KnowsNetworkDrivers_Only()
    {
        foreach (var d in new[] { "ndis.sys", "tcpip.sys", "e1d68x64.sys", "rt640x64.sys", "Netwtw10.sys", "NDIS.SYS" }) T.True(NetDpcCheck.IsNetDriver(d), d + " is a network driver");
        foreach (var d in new[] { "nvlddmkm.sys", "storport.sys", "ndis.sys.bak", "", null }) T.False(NetDpcCheck.IsNetDriver(d), (d ?? "null") + " is not");
        T.Eq(NetDpcCheck.Verdict(new DriverStat[0]), null, "no drivers: no message");
    }

    [Test] public static void NetBindings_UndoBindsEverythingItUnbound()
    {
        var t = Catalog.All.First(x => x.Id == "net_bindings");
        string on = string.Join(" ", t.ApplyCmds.Select(c => c[1]).ToArray()), off = string.Join(" ", t.RevertCmds.Select(c => c[1]).ToArray());
        foreach (var c in new[] { "ms_lldp", "ms_lltdio", "ms_rspndr", "ms_implat" })
        {
            T.True(on.Contains(c), "unbinds " + c);
            T.True(off.Contains(c), "undo binds " + c + " again");
        }
        T.False(on.Contains("ms_pacer"), "QoS Packet Scheduler is its own tweak");
        var q = Catalog.All.First(x => x.Id == "net_pacer");
        T.Eq(q.Risk, Risk.Advanced, "net_pacer is advanced");
        T.False(q.Presets.Any(), "net_pacer is in no preset");
    }
}
