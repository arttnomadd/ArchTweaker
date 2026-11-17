// Tests for src/Safety/.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ArchTweaker;

static class SafetyTests
{
    [Test] public static void Backups_OnlyRegFilesInsideTheBackupFolder()
    {
        string outside = Path.Combine(Path.GetTempPath(), "evil-" + Guid.NewGuid().ToString("N") + ".reg");
        File.WriteAllText(outside, "Windows Registry Editor Version 5.00");
        try { T.False(Backups.Ok(outside), "outside the backup folder"); }
        finally { File.Delete(outside); }
        T.False(Backups.Ok(Path.Combine(Paths.Backups, "missing.reg")), "missing file");
        T.False(Backups.Ok(Path.Combine(Paths.Backups, "..", "undo.journal")), "traversal / wrong type");
    }

    [Test] public static void Reassert_SkipsDangerZoneWhileLocked()
    {
        var t = Catalog.All.First(x => x.Id == "dz_bitlocker");
        T.False(DangerZone.Unlocked, "locked");
        int n = Drift.Reassert(new List<DriftItem> { new DriftItem { Tweak = t, DesiredApplied = true, Actual = false } });
        T.Eq(n, 0, "nothing re-applied while locked");
    }

    [Test] public static void UndoNotes_OnTweaksWithNothingToUndo()
    {
        foreach (var id in new[] { "cmd_trim", "cmd_autotuning", "cmd_rss", "cmd_timerclean" })
            T.Eq(Catalog.All.First(x => x.Id == id).UndoNote, "defaults", id + " restores defaults");
        T.Eq(Catalog.All.First(x => x.Id == "cmd_dupplans").UndoNote, "permanent", "duplicate-plan cleanup can't be undone");
        foreach (var t in Catalog.All.Where(x => x.Ops.Count == 0 && x.RevertCmds.Count == 0 && x.RevertExtra == null && (x.ApplyCmds.Count > 0 || x.ApplyExtra != null)))
            T.True(t.UndoNote != null, t.Id + " has no undo path, so it has an undo note");
    }

    [Test] public static void ExactUndo_KeepsYourOwnValue()
    {
        var kr = Catalog.All.First(x => x.Id == "keyrepeat");
        var k = kr.Ops.OfType<KeepOp>().FirstOrDefault(o => o.Name == "KeyboardSpeed");
        T.True(k != null && k.H == Hive.HKCU, "KeyboardSpeed restores what you had");
        T.False(Catalog.All.SelectMany(t => t.Ops.OfType<VOp>()).Any(v => v.On != null && Reg.Norm(v.On) == Reg.Norm(v.Off)), "no tweak undoes by writing its own value again");
        const string P = @"Software\ArchTweakerTest\Keep";
        try
        {
            Reg.Set(Hive.HKCU, P, "S", Microsoft.Win32.RegistryValueKind.String, "20");
            var op = new KeepOp { H = Hive.HKCU, Path = P, Name = "S", Kind = Microsoft.Win32.RegistryValueKind.String, Value = "31" };
            op.Apply(); T.True(op.IsOn(), "applied"); op.Revert();
            T.Eq(Reg.Norm(Reg.Get(Hive.HKCU, P, "S")), "20", "your 20 is back, not the default 31");
        }
        finally { Reg.DeleteTreeRaw(Hive.HKCU, @"Software\ArchTweakerTest"); }
    }

    [Test] public static void V39_SnapshotPrune_KeepsNewest()
    {
        string d = Path.Combine(Path.GetTempPath(), "at-snap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        try
        {
            for (int i = 0; i < 13; i++) { string f = Path.Combine(d, "apply_" + i + ".reg"); File.WriteAllText(f, "r"); File.SetLastWriteTimeUtc(f, new DateTime(2026, 1, 1).AddHours(i)); }
            T.Eq(RegSnapshot.Prune(d, 10), 3, "three oldest deleted");
            T.True(!File.Exists(Path.Combine(d, "apply_0.reg")) && File.Exists(Path.Combine(d, "apply_12.reg")), "oldest gone, newest kept");
        }
        finally { Directory.Delete(d, true); }
    }

    [Test] public static void RegRestore_Bytes_MatchRegistryFormat()
    {
        int t;
        T.Eq(BitConverter.ToString(RegRestore.Bytes(Microsoft.Win32.RegistryValueKind.DWord, 0, out t)), "00-00-00-00", "dword 0"); T.Eq(t, 4, "REG_DWORD");
        T.Eq(BitConverter.ToString(RegRestore.Bytes(Microsoft.Win32.RegistryValueKind.DWord, 0xFFFFFFFFu, out t)), "FF-FF-FF-FF", "uint dword");
        T.Eq(BitConverter.ToString(RegRestore.Bytes(Microsoft.Win32.RegistryValueKind.QWord, 1L, out t)), "01-00-00-00-00-00-00-00", "qword"); T.Eq(t, 11, "REG_QWORD");
        T.Eq(BitConverter.ToString(RegRestore.Bytes(Microsoft.Win32.RegistryValueKind.String, "a", out t)), "61-00-00-00", "string ends in null"); T.Eq(t, 1, "REG_SZ");
        T.Eq(BitConverter.ToString(RegRestore.Bytes(Microsoft.Win32.RegistryValueKind.MultiString, new[] { "a", "b" }, out t)), "61-00-00-00-62-00-00-00-00-00", "multi string"); T.Eq(t, 7, "REG_MULTI_SZ");
    }

    [Test] public static void RecoveryKit_RegLines_AreExactRegFormat()
    {
        T.Eq(RecoveryKit.RegLine("Start", 4, Microsoft.Win32.RegistryValueKind.DWord), "\"Start\"=dword:00000004", "dword");
        T.Eq(RecoveryKit.RegLine("X", -1, Microsoft.Win32.RegistryValueKind.DWord), "\"X\"=dword:ffffffff", "negative dword");
        T.Eq(RecoveryKit.RegLine("P", @"C:\a ""b""", Microsoft.Win32.RegistryValueKind.String), "\"P\"=\"C:\\\\a \\\"b\\\"\"", "string escaped");
        T.Eq(RecoveryKit.RegLine("", "v", Microsoft.Win32.RegistryValueKind.String), "@=\"v\"", "default value");
        T.Eq(RecoveryKit.RegLine("Gone", null, Microsoft.Win32.RegistryValueKind.Unknown), "\"Gone\"=-", "missing value is deleted");
        T.Eq(RecoveryKit.RegLine("Q", 1L, Microsoft.Win32.RegistryValueKind.QWord), "\"Q\"=hex(b):01,00,00,00,00,00,00,00", "qword");
        T.Eq(RecoveryKit.RegLine("E", "%a%", Microsoft.Win32.RegistryValueKind.ExpandString), "\"E\"=hex(2):25,00,61,00,25,00,00,00", "expand string");
    }

    [Test] public static void RecoveryKit_MapsToTheOfflineRegistry()
    {
        string ccs = RecoveryKit.Offline(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\WinDefend");
        T.True(ccs.StartsWith(@"HKEY_LOCAL_MACHINE\AT_SYSTEM\ControlSet00") && ccs.EndsWith(@"\Services\WinDefend"), "CurrentControlSet → ControlSet00N: " + ccs);
        T.Eq(RecoveryKit.Offline(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows Defender"), @"HKEY_LOCAL_MACHINE\AT_SOFTWARE\Policies\Microsoft\Windows Defender", "SOFTWARE");
        T.Eq(RecoveryKit.Offline(Hive.HKCU, @"Control Panel\Keyboard"), @"HKEY_USERS\AT_USER\Control Panel\Keyboard", "HKCU → loaded NTUSER.DAT");
        T.Eq(RecoveryKit.ForRecovery("/set hypervisorlaunchtype auto"), "/set {default} hypervisorlaunchtype auto", "bcdedit targets Windows, not Recovery");
        T.Eq(RecoveryKit.ForRecovery("/deletevalue {current} nx"), "/deletevalue {current} nx", "explicit entry kept");
    }

    [Test] public static void RecoveryKit_CapturesOriginals_AndReleases()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "at-kit-" + Guid.NewGuid().ToString("N"));
        RecoveryKit.FolderOverride = dir;
        string K = @"SOFTWARE\ArchTweakerTest\Kit" + Guid.NewGuid().ToString("N");   // 3.23: the kit prefers the journal's original, so a fresh key per run
        try
        {
            Reg.SetRaw(Hive.HKLM, K, "A", Microsoft.Win32.RegistryValueKind.DWord, 7);   // test setup: not a tweak, not journaled
            var t = new Tweak { Id = "dz_test", Category = DangerZone.CAT, Title = "test" };
            t.Ops.Add(new VOp { H = Hive.HKLM, Path = K, Name = "A", On = 0, Off = 1, Kind = Microsoft.Win32.RegistryValueKind.DWord });
            t.Ops.Add(new VOp { H = Hive.HKLM, Path = K, Name = "B", On = 1, Off = null, Kind = Microsoft.Win32.RegistryValueKind.DWord });
            t.ApplyCmds.Add(new[] { "bcdedit.exe", "/set nx AlwaysOff" }); t.RevertCmds.Add(new[] { "bcdedit.exe", "/set nx OptIn" });
            RecoveryKit.Capture(t);
            string reg = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "undo.reg"));
            T.True(reg.Contains(@"[HKEY_LOCAL_MACHINE\AT_SOFTWARE\" + K.Substring(9) + "]") && reg.Contains("\"A\"=dword:00000007") && reg.Contains("\"B\"=-"), "original 7 restored, new value B deleted:\n" + reg);
            string cmd = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "recovery.cmd"));
            T.True(cmd.Contains("reg load HKLM\\AT_SOFTWARE") && cmd.Contains("bcdedit /set {default} nx OptIn"), "script loads hives and undoes the boot setting");
            Reg.SetRaw(Hive.HKLM, K, "A", Microsoft.Win32.RegistryValueKind.DWord, 0);   // as if applied
            RecoveryKit.Capture(t);
            T.True(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "undo.reg")).Contains("\"A\"=dword:00000007"), "a second capture keeps the first original");
            RecoveryKit.Release("dz_test");
            T.False(System.IO.Directory.Exists(dir), "kit removed when its last tweak is undone");
        }
        finally { RecoveryKit.FolderOverride = null; Reg.DeleteTreeRaw(Hive.HKLM, @"SOFTWARE\ArchTweakerTest"); try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    [Test] public static void RecoveryKit_NothingToSave_StillCovered()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "at-kit-" + Guid.NewGuid().ToString("N"));
        RecoveryKit.FolderOverride = dir;
        try
        {
            var t = new Tweak { Id = "pro_missing_svc", Category = "Tasks & Services", Title = "test" };
            t.Ops.Add(new SvcOp { Name = "ArchTweakerNoSuchService" + Guid.NewGuid().ToString("N"), On = 4, Off = 3 });
            RecoveryKit.Capture(t);
            T.True(RecoveryKit.Covered().Contains("pro_missing_svc"), "a tweak with nothing to save is marked, so the backfill does not repeat it");
        }
        finally { RecoveryKit.FolderOverride = null; try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    // 3.13: history entries come newest first; only the newest is "last"; removing a line keeps the rest
    [Test] public static void History_EntriesAndRemove()
    {
        string f = System.IO.Path.Combine(Paths.Root, "history.txt"), bak = f + ".testbak";
        bool had = System.IO.File.Exists(f); if (had) System.IO.File.Copy(f, bak, true);
        try
        {
            if (System.IO.File.Exists(f)) System.IO.File.Delete(f);
            var a = Catalog.All[0]; var b = Catalog.All[1];
            History.Push(new List<KeyValuePair<Tweak, bool>> { new KeyValuePair<Tweak, bool>(a, true) });
            History.Push(new List<KeyValuePair<Tweak, bool>> { new KeyValuePair<Tweak, bool>(b, true), new KeyValuePair<Tweak, bool>(a, false) });
            var e = History.Entries();
            T.Eq(e.Count, 2, "two sessions");
            T.True(e[0].IsLast && !e[1].IsLast, "newest first and marked last");
            T.Eq(e[0].Items.Count, 2, "items of the newest session");
            History.RemoveLine(e[1].Line);
            e = History.Entries();
            T.Eq(e.Count, 1, "older session removed");
            T.True(e[0].IsLast && e[0].Items[0].Key == b, "newest session kept");
        }
        finally { try { System.IO.File.Delete(f); if (had) System.IO.File.Move(bak, f); } catch { } }
    }

    // 3.14: the latency note lands on the newest history line and survives a read
    [Test] public static void History_NoteLast()
    {
        string f = System.IO.Path.Combine(Paths.Root, "history.txt"), bak = f + ".testbak";
        bool had = System.IO.File.Exists(f); if (had) System.IO.File.Copy(f, bak, true);
        try
        {
            if (System.IO.File.Exists(f)) System.IO.File.Delete(f);
            var a = Catalog.All[0];
            History.Push(new List<KeyValuePair<Tweak, bool>> { new KeyValuePair<Tweak, bool>(a, true) });
            History.NoteLast("248"); History.NoteLast("251");
            var e = History.Entries();
            T.Eq(e.Count, 1, "still one session");
            T.Eq(e[0].LatencyUs, "251", "the newest note replaces the older one");
            T.True(History.HasLast && History.Last().Count == 1, "Undo last still reads the line");
        }
        finally { try { System.IO.File.Delete(f); if (had) System.IO.File.Move(bak, f); } catch { } }
    }

    // 3.17: .reg export parsing for the change history
    [Test] public static void ChangeHistory_ParseReg()
    {
        var reg = new[] {
            "Windows Registry Editor Version 5.00", "",
            "[HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\FileSystem]",
            "\"NtfsDisableLastAccessUpdate\"=dword:80000002",
            "\"Name \\\"q\\\"\"=\"text\"",
            "@=\"def\"",
            "\"Bin\"=hex:01,02,\\",
            "  03,04",
            "[HKEY_CURRENT_USER\\Control Panel\\Mouse]",
            "\"MouseSpeed\"=\"0\"" };
        var d = ChangeHistory.ParseReg(reg);
        T.Eq(d.Count, 2, "two keys");
        var fs = d["HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\FileSystem"];
        T.Eq(ChangeHistory.Pretty(fs["NtfsDisableLastAccessUpdate"]), "2147483650 (0x80000002) dword", "dword shown like the app shows values");
        T.Eq(fs["Name \"q\""], "\"text\"", "escaped quotes in a value name");
        T.Eq(fs[""], "\"def\"", "default value");
        T.Eq(ChangeHistory.Pretty(fs["Bin"]), "hex:01,02,03,04", "hex value continued on the next line");
        T.True(d.ContainsKey("HKEY_CURRENT_USER\\Control Panel\\Mouse"), "key names are case-insensitive and kept whole");
        T.Eq(ChangeHistory.SnapshotFor(new DateTime(1990, 1, 1)), null, "no snapshot that old");
    }

    [Test] public static void DriftGuard_KeyOrderFree()
    {
        T.Eq(DriftGuard.KeyOf(new[] { "b", "a", "c" }), "a,b,c", "sorted");
        T.Eq(DriftGuard.KeyOf(new[] { "c", "a", "b" }), DriftGuard.KeyOf(new[] { "a", "b", "c" }), "same set, same key");
    }

    [Test] public static void DriftWatch_Due()
    {
        var t0 = new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        T.False(DriftWatch.Due(t0, t0, t0, t0.AddMinutes(30)), "nothing changed, 30 min: not due");
        T.True(DriftWatch.Due(t0, t0.AddMinutes(5), t0, t0.AddMinutes(30)), "a driver or update log changed: due");
        T.True(DriftWatch.Due(t0, t0, t0, t0 + DriftWatch.Full), "3 hours: due anyway");
    }

    [Test] public static void RecoveryKit_LedgerRows_OnlyWhatCaptureWrites()
    {
        const string K = @"HKEY_LOCAL_MACHINE\AT_SYSTEM\ControlSet001\Control";
        foreach (var line in new[] {
            RecoveryKit.RegLine("A", 1, Microsoft.Win32.RegistryValueKind.DWord), RecoveryKit.RegLine("B", 5L, Microsoft.Win32.RegistryValueKind.QWord),
            RecoveryKit.RegLine("C", "x \"q\" \\ y", Microsoft.Win32.RegistryValueKind.String), RecoveryKit.RegLine("D", "%x%", Microsoft.Win32.RegistryValueKind.ExpandString),
            RecoveryKit.RegLine("E", new[] { "a", "b" }, Microsoft.Win32.RegistryValueKind.MultiString), RecoveryKit.RegLine("F", new byte[] { 1, 2 }, Microsoft.Win32.RegistryValueKind.Binary),
            RecoveryKit.RegLine("", "v", Microsoft.Win32.RegistryValueKind.String), RecoveryKit.RegLine("G", null, Microsoft.Win32.RegistryValueKind.Unknown) })
            T.True(RecoveryKit.RowOk(new[] { "t", K, line }), "own line accepted: " + line);
        T.True(RecoveryKit.RegLine("H", "a\r\n[HKEY_LOCAL_MACHINE\\X]", Microsoft.Win32.RegistryValueKind.String).Contains("=hex(1):"), "a value with a line break is written as hex(1)");
        T.True(RecoveryKit.RowOk(new[] { "CMD", "t", "/deletevalue {default} useplatformclock" }), "bcdedit undo accepted");
        T.False(RecoveryKit.RowOk(new[] { "CMD", "t", "/set x & del C:\\x" }), "command chaining rejected");
        T.False(RecoveryKit.RowOk(new[] { "CMD", "t", "/import evil" }), "other bcdedit verbs rejected");
        T.False(RecoveryKit.RowOk(new[] { "t", @"HKEY_LOCAL_MACHINE\SYSTEM\X", "\"A\"=dword:00000001" }), "a live hive rejected");
        T.False(RecoveryKit.RowOk(new[] { "t", K + "]\r\n[HKEY_LOCAL_MACHINE\\X", "\"A\"=dword:00000001" }), "key injection rejected");
        T.False(RecoveryKit.RowOk(new[] { "t", K, "\"A\"=dword:1\r\n\"B\"=\"x\"" }), "line injection rejected");
    }

    // 3.32
    [Test] public static void RegSnapshot_OnlyTouchedValues_AndParsesBack()
    {
        var vals = new[]
        {
            new RegSnapshot.SnapValue { H = Hive.HKLM, Path = @"SYSTEM\CurrentControlSet\Control", Name = "WaitToKillServiceTimeout", KeyExists = true, Value = "5000", Kind = Microsoft.Win32.RegistryValueKind.String },
            new RegSnapshot.SnapValue { H = Hive.HKLM, Path = @"SYSTEM\CurrentControlSet\Control", Name = "Missing", KeyExists = true, Value = null, Kind = Microsoft.Win32.RegistryValueKind.Unknown },
            new RegSnapshot.SnapValue { H = Hive.HKCU, Path = @"Control Panel\Desktop", Name = "HungAppTimeout", KeyExists = true, Value = 7, Kind = Microsoft.Win32.RegistryValueKind.DWord },
            new RegSnapshot.SnapValue { H = Hive.HKLM, Path = @"SOFTWARE\Policies\New", Name = "X", KeyExists = false },
        };
        string txt = RegSnapshot.Text(vals, "apply", new DateTime(2026, 11, 16, 1, 2, 3));
        var lines = txt.Split(new[] { "\r\n" }, StringSplitOptions.None);
        T.Eq(lines[0], "Windows Registry Editor Version 5.00", "reg header");
        T.Eq(lines.Count(l => l.StartsWith("[")), 2, "one header per existing key, no subkeys");
        T.True(txt.Contains("\"WaitToKillServiceTimeout\"=\"5000\""), "string value");
        T.True(txt.Contains("\"Missing\"=-"), "missing value deletes on import");
        T.True(txt.Contains("\"HungAppTimeout\"=dword:00000007"), "dword value");
        T.True(txt.Contains("; [HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\New] did not exist"), "missing key is a comment only");
        T.False(txt.Contains("[-"), "never deletes a key");
        var d = ChangeHistory.ParseReg(lines, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control" });
        T.Eq(d.Count, 1, "keys outside the wanted set are skipped");
        var c = d[@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control"];
        T.Eq(ChangeHistory.Pretty(c["Missing"]), "(not set)", "=- shows as not set");
        T.Eq(ChangeHistory.Pretty(c["WaitToKillServiceTimeout"]), "\"5000\"", "value read back");
    }

    [Test] public static void RegSnapshot_DeleteLarge_KeepsSmall()
    {
        string d = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "at-snap-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(d);
        try
        {
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(d, "apply_old.reg"), new byte[2000]);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(d, "apply_new.reg"), new byte[100]);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(d, "keep.txt"), new byte[5000]);
            T.Eq(RegSnapshot.DeleteLarge(d, 1000), 1, "one large .reg deleted");
            T.True(System.IO.File.Exists(System.IO.Path.Combine(d, "apply_new.reg")), "small snapshot stays");
            T.True(System.IO.File.Exists(System.IO.Path.Combine(d, "keep.txt")), "other files stay");
        }
        finally { try { System.IO.Directory.Delete(d, true); } catch { } }
    }
}
