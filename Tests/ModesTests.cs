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

    // 3.38.3: what Lite hides and Pro shows, with the mode set by the test (not read from this PC)
    [Test] public static void Mode_LiteHidesProThings_ProShowsAll()
    {
        var f = typeof(Mode).GetField("pro", BindingFlags.NonPublic | BindingFlags.Static);
        object keep = f.GetValue(null);
        var risky = Catalog.All.First(t => !Mode.LiteTweak(t));
        var safe = Catalog.All.First(Mode.LiteTweak);
        try
        {
            f.SetValue(null, (bool?)false);
            T.True(Mode.Lite && !Mode.Pro, "Lite");
            T.False(Mode.Allows(risky), "Lite hides " + risky.Id); T.True(Mode.Allows(safe), "Lite shows " + safe.Id);
            T.False(Mode.ShowsPage("Drivers"), "Lite hides a Pro page"); T.True(Mode.ShowsPage("Home"), "Lite shows Home");
            T.False(Mode.ShowsCategory(DangerZone.CAT), "Lite hides Danger zone"); T.True(Mode.ShowsCategory(safe.Category), "Lite shows " + safe.Category);
            T.False(Mode.ProPage(null), "no page name is not a Pro page");
            f.SetValue(null, (bool?)true);
            T.True(Mode.Allows(risky) && Mode.ShowsPage("Drivers") && Mode.ShowsCategory(DangerZone.CAT), "Pro shows everything");
            T.Eq(Mode.HiddenApplied(), 0, "Pro hides nothing");
        }
        finally { f.SetValue(null, keep); }
    }

    // a misspelt name in ProPages would leave that page open in Lite
    [Test] public static void Mode_EveryProPage_IsARealPage()
    {
        string root = TestHelp.RepoRoot();
        if (TestHelp.SkipWithoutSources(root)) return;
        string mf = File.ReadAllText(Path.Combine(root, "src", "Ui", "MainForm.cs"));
        var nav = new HashSet<string>(Regex.Matches(mf, @"AddNav\(nav, (?:null|""[^""]+""), ""([^""]+)""").Cast<Match>().Select(m => m.Groups[1].Value));
        var pro = (HashSet<string>)typeof(Mode).GetField("ProPages", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        T.True(pro.Count >= 10, "Pro pages listed");
        foreach (var p in pro) T.True(nav.Contains(p), "Pro page '" + p + "' is in the sidebar");
    }

    [Test] public static void ProPin_RejectsDamagedOrWrongInput()
    {
        string h = ProPin.Hash("4821", new byte[16]);
        T.False(ProPin.Check(null, "4821"), "no stored hash");
        T.False(ProPin.Check("", "4821"), "empty stored hash");
        T.False(ProPin.Check("not base64!:abc", "4821"), "salt is not base64");
        T.False(ProPin.Check(h + "A", "4821"), "hash with an extra character");
        T.False(ProPin.Check(h.Substring(0, h.Length - 1), "4821"), "hash with a character missing");
        T.False(ProPin.Check(h + ":x", "4821"), "three parts");
        T.False(ProPin.Check(h, null), "no PIN typed");
        T.False(ProPin.Check(h, ""), "empty PIN");
        T.False(ProPin.Check(h, " 4821"), "a space is not ignored");
        T.False(ProPin.Valid(null) || ProPin.Valid("") || ProPin.Valid("12 34") || ProPin.Valid("-1234"), "invalid PINs");
        T.True(ProPin.Valid("123456789012") && !ProPin.Valid("1234567890123"), "12 digits at most");
        string a = ProPin.NewHash("1111"), b = ProPin.NewHash("1111");
        T.False(ProPin.Check(a, "1112"), "a salted hash still rejects a wrong PIN");
        T.True(a.Split(':')[0] != b.Split(':')[0], "each hash has its own salt");
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
