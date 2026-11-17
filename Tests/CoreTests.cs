// Tests for src/Core.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ArchTweaker;

static class CoreTests
{
    [Test] public static void Clear_DropsCachedStates_AndBumpsGeneration()
    {
        var t = Catalog.All.First(x => x.Applicable);
        StateCache.Applied(t);
        T.True(StateCache.Get(t).HasValue, "state cached after a read");
        int g = StateCache.Generation;
        StateCache.Clear();
        T.False(StateCache.Get(t).HasValue, "cleared");
        T.True(StateCache.Generation == g + 1, "generation bumped");
    }

    [Test] public static void SelfTest_PutsEveryValueBack()
    {
        const string K = @"Software\ArchTweakerTest\Self";
        try
        {
            Reg.Set(Hive.HKCU, K, "Keep", Microsoft.Win32.RegistryValueKind.DWord, 7);   // user had 7; the tweak's undo writes 1
            var t = new Tweak { Id = "selftest_probe", Category = "System", Title = "t", Desc = "d" }
                .Dw(Hive.HKCU, K, "Keep", 0, 1).Dw(Hive.HKCU, K, "Missing", 1, null);
            int pass, fail;
            string rep = SelfTest.Run(new[] { t }, out pass, out fail);
            T.Eq(pass, 1, "passes: " + rep.Replace("\n", " | "));
            T.Eq(Reg.Norm(Reg.Get(Hive.HKCU, K, "Keep")), "7", "original 7 written back, not the undo value");
            T.True(Reg.Get(Hive.HKCU, K, "Missing") == null, "value that didn't exist is gone again");
            T.False(SelfTest.Testable(Catalog.All.First(x => x.Id == "defender")), "Danger zone never self-tested");
            T.False(SelfTest.Testable(Catalog.All.First(x => x.Id == "cmd_rsc")), "command tweaks skipped");
        }
        finally { Reg.DeleteTreeRaw(Hive.HKCU, @"Software\ArchTweakerTest"); }
    }

    [Test] public static void SvcPrevOp_RestoresTheOriginalStartType()
    {
        const string S = @"SYSTEM\CurrentControlSet\Services\ArchTweakerTestSvc";
        try
        {
            Reg.Set(Hive.HKLM, S, "Start", Microsoft.Win32.RegistryValueKind.DWord, 3);
            var op = new SvcPrevOp { Name = "ArchTweakerTestSvc" };
            T.False(op.IsOn(), "manual is not off");
            op.Apply();
            T.True(op.IsOn(), "disabled");
            op.Revert();
            T.Eq(Svc.GetStart("ArchTweakerTestSvc"), 3, "back to Manual");
        }
        finally { Reg.DeleteTreeRaw(Hive.HKLM, S); Reg.DeleteValue(Hive.HKLM, @"SOFTWARE\ArchTweaker\SvcPrev", "ArchTweakerTestSvc"); }
    }

    [Test] public static void KeepOp_RestoresTheExactOriginal()
    {
        const string K = @"SOFTWARE\ArchTweakerTest\Keep";
        try
        {
            Reg.Set(Hive.HKLM, K, "V", Microsoft.Win32.RegistryValueKind.DWord, 7);
            var op = new KeepOp { Path = K, Name = "V", Value = 1 };
            op.Apply(); T.True(op.IsOn(), "applied");
            op.Revert(); T.Eq(Reg.Norm(Reg.Get(Hive.HKLM, K, "V")), "7", "back to 7, not a guessed default");
            var op2 = new KeepOp { Path = K, Name = "Missing", Value = 1 };
            op2.Apply(); op2.Revert(); T.True(Reg.Get(Hive.HKLM, K, "Missing") == null, "a value that didn't exist is removed again");
        }
        finally { Reg.DeleteTreeRaw(Hive.HKLM, @"SOFTWARE\ArchTweakerTest"); }
    }

    [Test] public static void KeyDelOp_Recreate_RestoresAnEmptyKeyWithoutBackup()
    {
        const string K = @"SOFTWARE\ArchTweakerTest\NoBackupKey";
        try
        {
            Reg.EnsureKey(Hive.HKLM, K);
            var op = new KeyDelOp { H = Hive.HKLM, Path = K, Recreate = true };
            Reg.DeleteTreeRaw(Hive.HKLM, K);   // as if deleted with no .reg backup
            T.True(op.IsOn(), "key gone");
            op.Revert();
            T.True(Reg.KeyExists(Hive.HKLM, K), "re-created");
            T.True(Catalog.All.First(x => x.Id == "3dobjects").Ops.OfType<KeyDelOp>().All(o => o.Recreate), "3D Objects uses it");
        }
        finally { Reg.DeleteTreeRaw(Hive.HKLM, @"SOFTWARE\ArchTweakerTest"); }
    }

    [Test] public static void SelfTest_CoversEveryReversibleOpType()
    {
        int full = Catalog.All.Count(SelfTest.Testable);
        T.True(full > 160, "self-test runs apply/undo on most tweaks (" + full + ")");
        foreach (var id in new[] { "sysmain", "tasks_telemetry", "pp_parking", "gd_msi", "windowedopt" })
        {
            var t = Catalog.All.FirstOrDefault(x => x.Id == id);
            if (t != null && t.Applicable) T.True(SelfTest.Testable(t), id + " (" + t.Ops[0].GetType().Name + ") is tested, not just read");
        }
        T.Eq(SelfTest.WhyNot(Catalog.All.First(x => x.Id == "cmd_trim")), "no undo", "commands with no undo are read only");
        T.True(Catalog.All.Where(x => x.Dangerous).All(x => !SelfTest.Testable(x)), "Danger zone never changed by the self-test");
    }

    [Test] public static void SelfTest_RestoresServiceStartExactly()
    {
        // on Mono the service store is the registry: start type 3, tweak wants 4 and "undoes" to 2; the test must put back 3
        const string SV = @"SYSTEM\CurrentControlSet\Services\ArchTestSvc";
        try
        {
            Reg.Set(Hive.HKLM, SV, "Start", Microsoft.Win32.RegistryValueKind.DWord, 3);
            var t = new Tweak { Id = "test_svc", Category = "System", Title = "t" };
            t.Ops.Add(new SvcOp { Name = "ArchTestSvc", On = 4, Off = 2 });
            if (!Svc.Exists("ArchTestSvc")) { T.True(true, "service API not available on this runtime"); return; }
            int p, f, r; string rep = SelfTest.Run(new[] { t }, SelfTest.Kind.Full, out p, out f, out r);
            T.Eq(Svc.GetStart("ArchTestSvc"), 3, "start type back to 3, not the tweak's undo value 2: " + rep);
        }
        finally { Reg.DeleteTreeRaw(Hive.HKLM, SV); }
    }

    [Test] public static void SelfTest_ReadOnlyChangesNothing()
    {
        int p, f, r; string rep = SelfTest.Run(Catalog.All.Where(x => x.Category == "Interface").ToList(), SelfTest.Kind.ReadOnly, out p, out f, out r);
        T.Eq(p, 0, "read-only applies nothing"); T.True(r > 0, "states read: " + r);
    }

    [Test] public static void AfterApplyScan_Summary()
    {
        var prev = new LatencyRun { DpcUs = 600 }; var now = new LatencyRun { DpcUs = 300 };
        T.True(AfterApplyScan.Summary(now, prev).Contains("▼ 50 %"), "a drop shows as a down arrow with the per cent");
        T.True(AfterApplyScan.Summary(new LatencyRun { DpcUs = 620 }, prev).Contains("about the same"), "under 10 % is noise");
        T.True(AfterApplyScan.Summary(now, null).EndsWith("µs"), "no earlier scan: the number only");
    }

    // 3.30
    [Test] public static void ForegroundBoost_DecodesEveryValue()
    {
        T.Eq(ForegroundBoost.Values.Length, 12, "12 explicit values");
        T.Eq(ForegroundBoost.Values.Distinct().Count(), 12, "no duplicates");
        T.True(ForegroundBoost.Describe(0x26).Contains("short quanta") && ForegroundBoost.Describe(0x26).Contains("variable") && ForegroundBoost.Describe(0x26).Contains("3:1"), "0x26");
        T.True(ForegroundBoost.Describe(0x18).Contains("long quanta") && ForegroundBoost.Describe(0x18).Contains("fixed") && ForegroundBoost.Describe(0x18).Contains("1:1"), "0x18");
        T.True(ForegroundBoost.Describe(2).StartsWith("Windows default"), "2 is the Windows default");
        T.True(ForegroundBoost.BoostWorks(0x26), "variable + boost works");
        T.False(ForegroundBoost.BoostWorks(0x2A), "fixed quanta ignore the boost");
        T.False(ForegroundBoost.BoostWorks(0x24), "no boost");
    }

    [Test] public static void Repairs_ChainAndRuntimes()
    {
        var s = Repairs.ChainSteps();
        T.Eq(string.Join(",", s.Select(x => System.IO.Path.GetFileName(x.Exe)).ToArray()), "chkdsk.exe,sfc.exe,dism.exe,sfc.exe", "ChkDsk, SFC, DISM, SFC");
        T.True(s.All(x => x.Minutes > 0 && x.Exe == System.IO.Path.Combine(Environment.SystemDirectory, System.IO.Path.GetFileName(x.Exe))), "System32 paths and a time limit on every step");
        string sc = Repairs.RuntimeScript(@"C:\w\winget.exe");
        foreach (var id in Repairs.Runtimes) T.True(sc.Contains("--id " + id + " "), "installs " + id);
        T.True(sc.Contains("--accept-package-agreements"), "no prompt for agreements");
        T.Eq(Repairs.CleanName("  "), "ArchTweaker", "empty name");
        T.Eq(Repairs.CleanName("a\r\nb"), "ab", "control characters removed");
        T.Eq(Repairs.CleanName(new string('x', 100)).Length, 64, "64 characters at most");
    }

    // 3.35.1: Shell.RunLines (shared by the ISO bake and the repair chain): lines, exit code, timeout, start failure
    static bool Win { get { return Environment.OSVersion.Platform != PlatformID.Unix; } }
    // a shell command that prints one, "  two  " and err (on stderr), then exits 3
    static void Script(out string file, out string args)
    {
        if (Win) { file = "cmd.exe"; args = "/c \"echo one& echo   two  & echo err 1>&2& exit /b 3\""; }
        else { file = "/bin/sh"; args = "-c \"echo one; echo '  two  '; echo err 1>&2; exit 3\""; }
    }

    [Test] public static void RunLines_PassesLinesAndExitCode()
    {
        var got = new List<string>(); string tail, file, args; bool started;
        Script(out file, out args);
        int? code = Shell.RunLines(file, args, 10000, l => { lock (got) got.Add(l); }, false, out tail, out started);
        T.True(started, "started");
        T.Eq(code, (int?)3, "exit code passed back");
        T.True(got.Contains("one") && got.Contains("two") && got.Contains("err"), "stdout and stderr lines, trimmed: " + string.Join(",", got.ToArray()));
        T.True(tail.Contains("two"), "tail holds the last lines");
    }

    [Test] public static void RunLines_TimeoutReturnsNull_StartFailureReturnsMinusOne()
    {
        string tail; bool started;
        int? slow = Win ? Shell.RunLines("cmd.exe", "/c ping -n 6 127.0.0.1 >nul", 300, null, false, out tail, out started)
                        : Shell.RunLines("/bin/sh", "-c \"sleep 5\"", 300, null, false, out tail, out started);
        T.Eq(slow, (int?)null, "overran: null");
        T.True(started, "a timeout still counts as started");
        T.Eq(Shell.RunLines(Win ? @"C:\no\such\tool-xyz.exe" : "/no/such/tool-xyz", "", 1000, null, false, out tail, out started), (int?)(-1), "could not start: -1");
        T.False(started, "could not start: started is false, so a real exit code -1 can be told apart");
        T.True(tail.Length > 0, "the start error is in tail");
    }

    // 3.35.1: CLAUDE.md's source map names every file under src/ (3.35.0 missed 4 and named 36 only by short names)
    [Test] public static void ClaudeMap_NamesEverySourceFile()
    {
        string root = TestHelp.RepoRoot();
        if (TestHelp.SkipWithoutSources(root)) return;
        string map = File.ReadAllText(Path.Combine(root, "CLAUDE.md"));
        var named = new HashSet<string>(Regex.Matches(map, @"`(?:[\w/]+/)?([A-Za-z0-9_.]+\.cs)`").Cast<Match>().Select(m => m.Groups[1].Value), StringComparer.Ordinal);
        var missing = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetFileName(f)).Where(n => !named.Contains(n)).OrderBy(n => n).ToArray();
        T.Eq(missing.Length, 0, "add these to the source map in CLAUDE.md: " + string.Join(", ", missing));
        // the map is matched by file name, so names must stay unique across folders
        var dup = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories).GroupBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        T.Eq(dup.Length, 0, "two source files share a name: " + string.Join(", ", dup));
    }
}
