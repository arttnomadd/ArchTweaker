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
