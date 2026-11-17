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
