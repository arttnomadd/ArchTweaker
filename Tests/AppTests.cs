// Tests for src/App/.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ArchTweaker;

static class AppTests
{
    [Test] public static void MaskRx_MatchesExactExtension_NotLongerOnes()
    {
        var rx = (Regex)T.Call(typeof(Winapp), "MaskRx", "*.log");
        T.True(rx.IsMatch("app.log"), "*.log matches app.log");
        T.False(rx.IsMatch("app.log1"), "*.log must NOT match app.log1");
        T.False(rx.IsMatch("app.logs"), "*.log must NOT match app.logs");
    }

    [Test] public static void MaskRx_StarMeansMatchEverything()
    {
        T.Eq(T.Call(typeof(Winapp), "MaskRx", "*"), null, "* => null (match all)");
    }

    [Test] public static void Compare_OrdersVersionsNumerically()
    {
        T.True(Updater.Compare("2.0.1", "2.0") > 0, "2.0.1 > 2.0");
        T.True(Updater.Compare("2.0", "2.0.1") < 0, "2.0 < 2.0.1");
        T.Eq(Updater.Compare("2.0.0.0", "2.0"), 0, "2.0.0.0 == 2.0");
        T.True(Updater.Compare("10.0", "9.9") > 0, "10.0 > 9.9 (numeric, not lexical)");
    }

    [Test] public static void VersionRx_RejectsMalformedOrTraversalVersions()
    {
        var rx = (Regex)T.Field(typeof(Updater), "VersionRx");
        T.True(rx.IsMatch("2.0.1"), "2.0.1 valid");
        T.False(rx.IsMatch("9\\..\\..\\Windows"), "path-traversal version rejected");
        T.False(rx.IsMatch("2.0-rc1"), "non-numeric suffix rejected");
    }

    [Test] public static void Overlay_AllowsPolicyRoots_RejectsOthers()
    {
        T.True(Overlay.PathAllowed("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection"), "policies allowed");
        T.False(Overlay.PathAllowed("HKLM", @"SOFTWARE\Classes\exefile\shell\open\command"), "file associations rejected");
        T.False(Overlay.PathAllowed("HKLM", @"SOFTWARE\Policies\..\Classes"), "traversal rejected");
    }

    [Test] public static void Overlay_LoadsRegistryTweak_PatchesAndRejectsCommands()
    {
        string f = Path.Combine(Path.GetTempPath(), "at_overlay_test.xml");
        File.WriteAllText(f, "<overlay version=\"1\">" +
            "<tweak id=\"ov_test_one\" category=\"Privacy\" title=\"Test\" desc=\"d\" impact=\"1\"><dword hive=\"HKLM\" key=\"SOFTWARE\\Policies\\Test\" name=\"X\" on=\"1\" off=\"delete\"/></tweak>" +
            "<tweak id=\"ov_bad\" category=\"Privacy\" title=\"Bad\"><cmd exe=\"cmd.exe\"/></tweak>" +
            "<tweak id=\"ov_badkey\" category=\"Privacy\" title=\"Bad\"><dword hive=\"HKLM\" key=\"SOFTWARE\\Classes\\x\" name=\"X\" on=\"1\"/></tweak>" +
            "<patch id=\"existing\" title=\"Patched\"/></overlay>");
        try
        {
            var all = new List<Tweak> { new Tweak { Id = "existing", Category = "Privacy", Title = "Old", Desc = "" } };
            Overlay.ApplyTo(all, f);
            T.True(all.Any(t => t.Id == "ov_test_one" && t.Ops.Count == 1), "registry tweak loaded");
            T.False(all.Any(t => t.Id == "ov_bad" || t.Id == "ov_badkey"), "command / disallowed key rejected");
            T.True(all.First(t => t.Id == "existing").Title == "Patched", "patch applied");
        }
        finally { File.Delete(f); }
    }

    [Test] public static void LogSay_ReachesNoticeListeners_LogWDoesNot()
    {
        string got = null; Action<string> h = s => got = s;
        Log.Notice += h;
        try { Log.W("internal detail"); T.True(got == null, "W stays out of the status line"); Log.Say("3 changes applied"); T.True(got == "3 changes applied", "Say reaches it"); }
        finally { Log.Notice -= h; }
    }

    [Test] public static void LogFilter_ProblemsAndResults()
    {
        var detail = new LogStore.Entry { Text = "12:00:00  power read x", Kind = LogStore.Kind.Detail };
        var result = new LogStore.Entry { Text = "12:00:01  5 changes applied", Kind = LogStore.Kind.Result };
        var bad = new LogStore.Entry { Text = "12:00:02  FAILED x: denied", Kind = LogStore.Kind.Problem };
        T.True(LogStore.Pass(detail, 0) && LogStore.Pass(result, 0) && LogStore.Pass(bad, 0), "all shows everything");
        T.True(!LogStore.Pass(detail, 1) && LogStore.Pass(result, 1) && LogStore.Pass(bad, 1), "results hide details, keep problems");
        T.True(!LogStore.Pass(detail, 2) && !LogStore.Pass(result, 2) && LogStore.Pass(bad, 2), "problems only");
    }

    [Test] public static void LogStore_MarksSayLinesAsResults()
    {
        LogStore.Hook();
        Log.W("v32 detail line");
        Log.Say("v32 result line");
        Log.W("v32 FAILED something");
        string res = LogStore.Text(1), prob = LogStore.Text(2);
        T.True(res.Contains("v32 result line") && !res.Contains("v32 detail line"), "Say is a result, W is a detail");
        T.True(prob.Contains("v32 FAILED something") && !prob.Contains("v32 result line"), "FAILED is a problem");
    }

    [Test] public static void Overlay_RefusesKeysThatStartPrograms()
    {
        T.False(Overlay.PathAllowed("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run"), "Run key");
        T.False(Overlay.PathAllowed("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\x.exe"), "IFEO");
        T.False(Overlay.PathAllowed("HKLM", @"SYSTEM\CurrentControlSet\Services\X"), "service key");
        T.False(Overlay.PathAllowed("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"), "Winlogon");
        T.False(Overlay.PathAllowed("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Explorer\Run"), "policy Run");
        T.True(Overlay.PathAllowed("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection"), "plain policy still allowed");
    }

    [Test] public static void Overlay_RefusesProgramStartingValueNames()
    {
        string f = Path.Combine(Path.GetTempPath(), "ov-test-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(f, "<overlay version=\"1\"><tweak id=\"ov_bad\" category=\"Privacy\" title=\"x\"><string hive=\"HKCU\" key=\"Software\\Microsoft\\Foo\" name=\"Debugger\" on=\"evil.exe\" /></tweak></overlay>");
        try { var all = new List<Tweak>(); Overlay.ApplyTo(all, f); T.Eq(all.Count, 0, "Debugger value refused"); }
        finally { File.Delete(f); }
    }

    [Test] public static void Updater_RejectsOtherHosts()
    {
        T.False(Updater.HostAllowed("https://evil.example/latest.json"), "other host");
        T.False(Updater.HostAllowed("http://raw.githubusercontent.com/x"), "plain http");
        T.True(Updater.HostAllowed("https://objects.githubusercontent.com/x"), "GitHub release storage");
        T.True(Updater.HostAllowed("https://release-assets.githubusercontent.com/x"), "GitHub release storage (2025+ redirect)");
        T.True(Updater.DefaultManifest.StartsWith("https://raw.githubusercontent.com/arttnomadd/ArchTweaker/main/"), "default manifest is the project repo");
    }

    [Test] public static void StartupTrace_IsOffByDefault_AndShellTimesWhenOn()
    {
        T.False(StartupTrace.On, "no trace unless /trace");
        long got = -1; Shell.TraceDone = (f, a, ms) => got = ms;
        try { Shell.Run("definitely-not-a-program-xyz", "", true, 2000); } finally { Shell.TraceDone = null; }
        T.True(got >= 0, "Shell.Run reports its duration to the trace");
    }

    // ---- 3.35: signed update manifest (ADR-0008). The key here is a throwaway test key, not the release key.
    const string TestPub = @"<RSAKeyValue><Modulus>lhqiw8z5pEKq2vyP1KP3X0QHHplz1atLWF6tmK72DlDnfvCXeJ2P2fzNnmfd3r+fHzyynfCrDU0/v210RmiIRB64p4OqwVQwrOe7f5mEms6zdGY08VtFvueKuWDJoBrhPcnywgyfbCX6s6GBnk2zWk67LWXnnuE9eTEq0af2HMsoxRMIs2ic1V8IdIyVAxDlSFE8AjRqAMXArufbb0T1ZsiK/EjAcUVb+6T4lnO9cBB5xYjNi9XhKZn+YbgoTABFC+hniWL0TlgIzAuiZKEVNQ5532I59xpAZlMf/eo7l111WVeKMjc+aEIekSD8xAou80ub/Qh7RF26UV6W+A+oWw==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
    const string Signed = @"{""version"": ""9.9.9"", ""serial"": ""1000"", ""url"": ""https://github.com/example/at/releases/download/v9.9.9/ArchTweaker.exe"", ""sha256"": ""abababababababababababababababababababababababababababababababab"", ""notes"": ""test"", ""sig"": ""UAzOisVmpygjiDKVnxvbPlc3PvzaiRKlGQ3fiqvcWieciO2VoGNtq2KHwnrSPUrZJ6AWvrmilzg/hAWvWOinsYwHo+DIf8tLOvFifaPyxdsE+/CH4zJ0nX5n+8Bz2jFlMYeM40wTJvFjmkTumpfejdTdcXH9ev0/n2vzgEGoq2r+mgS7tbqRGo3pXJU4gkVN5k8ti3qhsOp4GUC9Q9wh7YzuhpfcEcXgVz7mknE854p6z0Xf3gGNn2CAzw7RyN9P2jE+BZ9IkAcfXxnU9faqdLKJ8KgxXhoDjaN0j/TvNhfa2KvCE8WRAwlKw/4cW+7Cuqjp77BTdr4oeg0GTfp69g==""}";

    [Test] public static void Manifest_SignedWithTheKey_Verifies()
    {
        T.True(Updater.SignatureOk(Signed, TestPub), "a manifest signed with the matching private key verifies");
    }

    [Test] public static void Manifest_ChangedUrlOrHash_FailsTheSignature()
    {
        T.False(Updater.SignatureOk(Signed.Replace("example/at", "attacker/at"), TestPub), "another download url breaks the signature");
        T.False(Updater.SignatureOk(Signed.Replace("abab", "cdcd"), TestPub), "another sha256 breaks the signature");
        T.False(Updater.SignatureOk(Signed.Replace("9.9.9", "9.9.8"), TestPub), "another version breaks the signature");
        T.False(Updater.SignatureOk(Signed.Replace("\"notes\": \"test\"", "\"notes\": \"run setup.exe\""), TestPub), "other notes break the signature");
    }

    [Test] public static void Manifest_Unsigned_OrOtherKey_IsRefused()
    {
        string noSig = Regex.Replace(Signed, ",\\s*\"sig\"\\s*:\\s*\"[^\"]*\"", "");
        T.False(Updater.SignatureOk(noSig, TestPub), "no sig field: refused");
        T.False(Updater.SignatureOk(Signed, Updater.PublicKeyXml), "signed with another key than the one in the exe: refused");
        T.False(Updater.SignatureOk(Signed.Replace("\"sig\": \"", "\"sig\": \"!!"), TestPub), "a sig that is not base64: refused, no crash");
    }

    [Test] public static void Manifest_SignedText_HasFixedFieldOrder()
    {
        T.Eq(Updater.SignedText("{\"url\":\"u\",\"version\":\"1.0\"}"), "archtweaker-manifest-v1\n1.0\n\nu\n\n\n\n", "fields in a fixed order, missing ones empty");
    }

    [Test] public static void Manifest_Serial_RefusesReplayOfAnOlderManifest()
    {
        T.True(Updater.SerialNewEnough(Signed, 0), "first manifest: accepted");
        T.True(Updater.SerialNewEnough(Signed, 1000), "the same serial again: accepted (a re-check)");
        T.False(Updater.SerialNewEnough(Signed, 1001), "older than one seen before: refused");
        T.False(Updater.SerialNewEnough(Signed.Replace("\"serial\": \"1000\", ", ""), 0), "no serial: refused");
    }

    [Test] public static void Manifest_DuplicateOrWrongCaseKey_ReadsAsMissing()
    {
        T.False(Updater.SignatureOk(Signed.Replace("{\"version\"", "{\"url\": \"https://github.com/x/y.exe\", \"version\""), TestPub), "a second url key: refused");
        T.False(Updater.SignatureOk(Signed.Replace("\"sig\"", "\"SIG\""), TestPub), "keys are case-sensitive");
    }

    [Test] public static void SwapScript_RefusesPathsCmdWouldMisread()
    {
        T.True(Updater.BatSafe(@"C:\Tools\ArchTweaker\ArchTweaker.exe"), "a plain path is fine");
        T.True(Updater.BatSafe(@"C:\Users\Анна\Desktop (old)\ArchTweaker.exe"), "spaces, brackets and Cyrillic are fine");
        foreach (var bad in new[] { @"C:\100%\a.exe", @"C:\a^b\a.exe", @"C:\a&b\a.exe", @"C:\a!b\a.exe", "" })
            T.False(Updater.BatSafe(bad), "refused: " + bad);
    }

    [Test] public static void Overlay_RefusesPersistenceAndProtectionKeys()
    {
        string[][] k = {
            new[] { "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders" },
            new[] { "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders" },
            new[] { "HKLM", @"SYSTEM\CurrentControlSet\Control\SecurityProviders" },
            new[] { "HKLM", @"SYSTEM\CurrentControlSet\Control\NetworkProvider\Order" },
            new[] { "HKLM", @"SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths" },
            new[] { "HKLM", @"SOFTWARE\Policies\Microsoft\Windows Defender" },
            new[] { "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" },
            new[] { "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers" },
        };
        foreach (var a in k) T.False(Overlay.PathAllowed(a[0], a[1]), "overlay refuses " + a[1]);
        T.True(Overlay.PathAllowed("HKCU", @"Software\Microsoft\GameBar"), "an ordinary settings key is still allowed");
        T.True(Overlay.PathAllowed("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\System"), "\\Policies\\System matches that key only, not Policies\\Microsoft\\Windows\\System");
        T.False(Overlay.PathAllowed("HKLM", @"SOFTWARE\Policies\Microsoft\SystemCertificates\Root"), "certificate stores: refused");
        T.True(Overlay.PathAllowed("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection"), "a policy key is still allowed");
    }
}
