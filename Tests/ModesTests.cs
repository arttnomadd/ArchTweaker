// Tests for src/Modes/.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ArchTweaker;

static class ModesTests
{
    [Test] public static void Mode_LiteRules()
    {
        T.True(Catalog.All.Where(t => t.Category == DangerZone.CAT).All(t => !Mode.LiteTweak(t)), "Danger zone is Pro only");
        T.True(Catalog.All.Where(t => t.Category == "Command tweaks").All(t => !Mode.LiteTweak(t)), "Command tweaks are Pro only");
        T.True(Catalog.All.Where(t => t.Experimental || t.Risk != Risk.Safe).All(t => !Mode.LiteTweak(t)), "experimental and risky tweaks are Pro only");
        T.True(Catalog.All.Count(Mode.LiteTweak) > 100, "Lite still has a full set of safe tweaks");
        T.True(Catalog.Categories.Where(c => c != DangerZone.CAT && c != "Command tweaks").All(c => Catalog.All.Any(t => t.Category == c && Mode.LiteTweak(t))), "every other category has Lite tweaks");
        foreach (var p in new[] { "Interrupt tuner", "Tuning assistant", "Bake into ISO", "Drivers", "Services", "Deep clean", "A/B test" }) T.True(Mode.ProPage(p), p + " is Pro only");
        foreach (var p in new[] { "Home", "Benchmark", "Hardware audit", "Latency", "Mouse polling", "Cleanup", "Change history", "Tools & backup", "Settings" }) T.True(!Mode.ProPage(p), p + " stays in Lite");
        T.True(ProDisclaimer.Points.Length >= 5 && ProDisclaimer.Points.Any(x => x.Contains("own risk")) && ProDisclaimer.Points.Any(x => x.Contains("Safe Mode")), "disclaimer names the risk and the repair tools");
    }

    [Test] public static void V323_ProTools()
    {
        string h = ProPin.Hash("4821", new byte[16]);
        T.True(ProPin.Check(h, "4821"), "right PIN"); T.True(!ProPin.Check(h, "4822"), "wrong PIN"); T.True(!ProPin.Check("broken", "4821"), "damaged hash");
        T.True(ProPin.NewHash("4821") != ProPin.NewHash("4821"), "salted"); T.True(ProPin.Check(ProPin.NewHash("123456"), "123456"), "new hash checks");
        T.True(ProPin.Valid("1234") && !ProPin.Valid("123") && !ProPin.Valid("12ab") && !ProPin.Valid("1234567890123"), "4 to 12 digits");
        var t = new Tweak { Id = "x_test", Title = "Test", Category = "System" };
        t.Ops.Add(new VOp { H = Hive.HKLM, Path = @"SOFTWARE\Test", Name = "A", Kind = Microsoft.Win32.RegistryValueKind.DWord, On = 1 });
        t.Ops.Add(new VOp { H = Hive.HKCU, Path = @"Software\Test", Name = "B", Kind = Microsoft.Win32.RegistryValueKind.String, On = null });
        t.Ops.Add(new SvcOp { Name = "SysMain", On = 4, Off = 2 });
        t.ApplyCmds.Add(new[] { "bcdedit", "/set disabledynamictick yes" });
        var reg = new System.Text.StringBuilder(); var cmd = new System.Text.StringBuilder();
        ProExport.Build(new[] { t }, reg, cmd);
        string r = reg.ToString(), c = cmd.ToString();
        T.True(r.StartsWith("Windows Registry Editor Version 5.00"), ".reg header");
        T.True(r.Contains(@"[HKEY_LOCAL_MACHINE\SOFTWARE\Test]") && r.Contains("\"A\"=dword:00000001"), "DWORD exported");
        T.True(r.Contains(@"[HKEY_CURRENT_USER\Software\Test]") && r.Contains("\"B\"=-"), "delete exported");
        T.True(c.Contains("sc.exe config \"SysMain\" start= disabled") && c.Contains("bcdedit /set disabledynamictick yes"), "service and command in .cmd");
        T.True(c.Contains("net session"), ".cmd checks for administrator");
        T.True(RescueBackfill.Covers(Catalog.All.First(x => x.Category == DangerZone.CAT)), "Danger zone goes to the recovery kit");
        T.True(!RescueBackfill.Covers(Catalog.All.First(Mode.LiteTweak)), "Lite tweaks need no recovery kit");
    }
}
