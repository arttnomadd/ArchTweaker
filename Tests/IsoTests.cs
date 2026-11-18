// Tests for src/Iso/.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ArchTweaker;

static class IsoTests
{
    [Test] public static void IsoBake_OfflinePath()
    {
        bool u;
        T.Eq(IsoBake.OfflinePath(Hive.HKLM, @"SOFTWARE\Policies\X", 1, out u), @"AT_ISO_SOFTWARE\Policies\X", "SOFTWARE"); T.False(u, "machine hive");
        T.Eq(IsoBake.OfflinePath(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\X", 2, out u), @"AT_ISO_SYSTEM\ControlSet002\Control\X", "CurrentControlSet maps to the image's control set");
        T.Eq(IsoBake.OfflinePath(Hive.HKLM, @"System\Setup", 1, out u), @"AT_ISO_SYSTEM\Setup", "SYSTEM, any case");
        T.Eq(IsoBake.OfflinePath(Hive.HKCU, @"Control Panel\Mouse", 1, out u), @"AT_ISO_DEFUSER\Control Panel\Mouse", "HKCU goes to the default user"); T.True(u, "users hive");
        T.Eq(IsoBake.OfflinePath(Hive.HKCR, @"Directory\shell", 1, out u), @"AT_ISO_SOFTWARE\Classes\Directory\shell", @"HKCR = SOFTWARE\Classes");
        T.Eq(IsoBake.OfflinePath(Hive.HKLM, @"HARDWARE\X", 1, out u), null, "HARDWARE is not in an image");
        T.Eq(IsoBake.OfflinePath(Hive.HKU, @"S-1-5-18\X", 1, out u), null, "HKU is not reachable");
    }

    [Test] public static void IsoBake_Unbakeable()
    {
        Func<Tweak> mk = () => new Tweak { Id = "x", Category = "Gaming", Title = "x" };
        T.Eq(IsoBake.Unbakeable(mk().Dw(Hive.HKLM, @"SOFTWARE\X", "v", 1, null).Dw(Hive.HKCU, @"Software\Y", "w", 0, 1)), null, "registry values bake");
        T.Eq(IsoBake.Unbakeable(mk().Service("SysMain", 4, 2)), null, "service start type bakes");
        T.Eq(IsoBake.Unbakeable(mk().Dw(Hive.HKLM, @"SOFTWARE\X", "v", 1, null).Cmd("bcdedit.exe", "/set x")), "runs commands", "commands don't");
        T.Eq(IsoBake.Unbakeable(mk().Tasks(@"\Microsoft\Windows\X")), "changes a scheduled task", "tasks don't");
        T.Eq(IsoBake.Unbakeable(mk()), "has no registry values", "empty tweak");
        T.Eq(IsoBake.Unbakeable(mk().Dw(Hive.HKLM, @"HARDWARE\X", "v", 1, null)), "writes outside the image's hives", "HARDWARE");
        T.Eq(IsoBake.Unbakeable(mk().Dw(Hive.HKLM, @"SOFTWARE\X", "v", 1, null).OnApply(delegate { })), "uses a built-in action", "built-in action");
    }

    // 3.38.3: the checks Run makes before it touches the system (no mount, no DISM, no hive load)
    [Test] public static void IsoBake_Run_RefusesBadJobs_BeforeTouchingTheSystem()
    {
        string iso = Path.GetTempFileName(), dir = Path.GetTempPath();
        try
        {
            Func<Tweak> ok = () => new Tweak { Id = "x", Category = "Gaming", Title = "Fine" }.Dw(Hive.HKLM, @"SOFTWARE\X", "v", 1, null);
            Func<IsoJob> job = () => { var j = new IsoJob { Iso = iso, OutDir = dir }; j.Editions.Add(new IsoEdition { Index = 1, Name = "Pro" }); j.Tweaks.Add(ok()); return j; };
            var j1 = job(); j1.Iso = Path.Combine(dir, "no-such-" + Guid.NewGuid().ToString("N") + ".iso");
            T.Eq(IsoBake.Run(j1, delegate { }).Error, "The ISO or the output folder is not valid.", "missing ISO");
            var j2 = job(); j2.OutDir = dir + "a\"b";
            T.Eq(IsoBake.Run(j2, delegate { }).Error, "The ISO or the output folder is not valid.", "quote in the output folder");
            var j3 = job(); j3.OutDir = Path.Combine(dir, "no-such-" + Guid.NewGuid().ToString("N"));
            T.Eq(IsoBake.Run(j3, delegate { }).Error, "The ISO or the output folder is not valid.", "missing output folder");
            var j4 = job(); j4.Editions.Clear();
            T.Eq(IsoBake.Run(j4, delegate { }).Error, "Choose at least one edition and one tweak.", "no edition");
            var j5 = job(); j5.Tweaks.Clear();
            T.Eq(IsoBake.Run(j5, delegate { }).Error, "Choose at least one edition and one tweak.", "no tweak");
            var j6 = job(); j6.Tweaks.Add(new Tweak { Id = "y", Category = "Gaming", Title = "Runs bcdedit" }.Dw(Hive.HKLM, @"SOFTWARE\X", "v", 1, null).Cmd("bcdedit.exe", "/set x"));
            string e6 = IsoBake.Run(j6, delegate { }).Error;
            T.True(e6 != null && e6.StartsWith("These tweaks can't be baked:") && e6.Contains("Runs bcdedit") && !e6.Contains("Fine"), "an unbakeable tweak is named");
        }
        finally { try { File.Delete(iso); } catch { } }
    }

    [Test] public static void IsoBake_ImportOscdimg_ChecksTheFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "at-osc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            T.Eq(IsoBake.ImportOscdimg(Path.Combine(dir, "missing.exe")), "The file does not exist.", "missing file");
            string txt = Path.Combine(dir, "notes.exe"); File.WriteAllText(txt, "hello");
            T.Eq(IsoBake.ImportOscdimg(txt), "This file is not a program.", "not MZ");
            string tiny = Path.Combine(dir, "one.exe"); File.WriteAllBytes(tiny, new byte[] { (byte)'M' });
            T.Eq(IsoBake.ImportOscdimg(tiny), "This file is not a program.", "too short");
            string other = Path.Combine(dir, "setup.exe"); File.WriteAllBytes(other, new byte[] { (byte)'M', (byte)'Z', 0, 0 });
            T.Eq(IsoBake.ImportOscdimg(other), "This file is not oscdimg.exe.", "a program with another name");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // paths reach PowerShell base64-encoded, so quotes and $() in a file name can't break out of the string
    [Test] public static void IsoBake_Ps1_RoundTripsAnyText()
    {
        foreach (var s in new[] { @"C:\ISO\Win 11.iso", "a'b\"c$(calc)`d;e|f", "Ünïcödé €", "" })
        {
            string ps = (string)T.Call(typeof(IsoBake), "Ps1", s);
            var m = Regex.Match(ps, @"FromBase64String\('([A-Za-z0-9+/=]*)'\)");
            T.True(m.Success, "base64 literal for " + s);
            T.Eq(System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(m.Groups[1].Value)), s, "round trip: " + s);
            T.False(ps.Contains("calc"), "no raw text in the command");
        }
    }

    [Test] public static void IsoBake_ParseAndArgs()
    {
        var e = IsoBake.ParseEditions("1|Windows 11 Home\r\n6|Windows 11 Pro\r\njunk\r\n0|bad\r\n|x\r\n");
        T.Eq(e.Count, 2, "two editions"); T.Eq(e[1].Index, 6, "index"); T.Eq(e[1].Name, "Windows 11 Pro", "name");
        T.Eq(IsoBake.Label("Win11_24H2_English x64"), "Win11_24H2_English_x64", "label sanitised");
        T.Eq(IsoBake.Label(new string('a', 40)).Length, 32, "label at most 32");
        T.Eq(IsoBake.Label("??"), "ARCHTWEAKER_WIN", "empty label falls back");
        string a = IsoBake.OscdimgArgs(@"D:\w\", @"D:\o.iso", "L", true, true);
        T.True(a.Contains(@"-bootdata:2#p0,e,b""D:\w\boot\etfsboot.com""#pEF,e,b""D:\w\efi\microsoft\boot\efisys.bin"""), "BIOS + UEFI boot data");
        T.True(a.EndsWith(@"""D:\w"" ""D:\o.iso""", StringComparison.Ordinal), "source and target last, trailing slash dropped");
        T.True(IsoBake.OscdimgArgs(@"D:\w", @"D:\o.iso", "L", false, true).Contains("-bootdata:1#pEF"), "UEFI only");
        T.False(IsoBake.SafePath(@"C:\a""b"), "quote rejected"); T.True(IsoBake.SafePath(@"C:\ISO\Win 11.iso"), "normal path ok");
    }
}
