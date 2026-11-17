// ArchTweaker: Recovery kit: offline .reg and recovery.cmd for Windows Recovery, and registry restore.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ---------------- recovery kit for Danger zone tweaks ----------------
    // Before a Danger zone tweak is applied, the values it is about to change are written to
    // <system drive>\ArchTweaker-Recovery as a .reg file for the OFFLINE registry, plus recovery.cmd that loads the
    // hives, imports it and runs the boot-setting undo commands. It runs from Windows Recovery's Command Prompt.
    public static class RecoveryKit
    {
        public static string FolderOverride;   // tests
        public static string Folder
        {
            get
            {
                if (FolderOverride != null) return FolderOverride;
                string root = null;
                try { root = Path.GetPathRoot(Environment.SystemDirectory); } catch { }
                return Path.Combine(string.IsNullOrEmpty(root) ? "C:\\" : root, "ArchTweaker-Recovery");
            }
        }
        static string Ledger { get { return Path.Combine(Folder, "ledger.tsv"); } }

        // ledger rows: id \t offline key \t .reg value line   ·   CMD \t id \t bcdedit arguments   ·   SKIP \t id \t what
        static List<string[]> Read()
        {
            try
            {
                if (!File.Exists(Ledger)) return new List<string[]>();
                // 3.27.1: the ledger becomes recovery.cmd and undo.reg, run offline with full rights: only an
                // administrator-owned ledger counts, and only rows this app could have written
                if (FolderOverride == null && !Secure.TrustedData(Ledger, "the recovery kit ledger")) return new List<string[]>();
                var rows = File.ReadAllLines(Ledger).Where(l => l.Length > 0).Select(l => l.Split('\t')).Where(r => r.Length == 3).ToList();
                int bad = rows.RemoveAll(r => !RowOk(r));
                if (bad > 0) Log.W("Recovery kit: ignored {0} ledger row(s) that this app does not write.", bad);
                return rows;
            }
            catch (Exception ex) { Log.W("Recovery kit: could not read the ledger: {0}", ex.Message); return new List<string[]>(); }
        }

        static readonly System.Text.RegularExpressions.Regex BcdArgs = new System.Text.RegularExpressions.Regex(@"^/(set|deletevalue) [A-Za-z0-9{}\-_. ]+$");
        static readonly System.Text.RegularExpressions.Regex RegValue = new System.Text.RegularExpressions.Regex(@"^(@|""(?:[^""\\\r\n]|\\.)*"")=(-|dword:[0-9a-f]{8}|hex(\([0-9a-f]+\))?:[0-9a-f,]*|""(?:[^""\\\r\n]|\\.)*"")$");
        // A ledger row as Capture writes it: a bcdedit /set or /deletevalue, a SKIP note, or one .reg value line under an offline key.
        public static bool RowOk(string[] r)
        {
            if (r == null || r.Length != 3 || r.Any(x => x.IndexOf('\r') >= 0 || x.IndexOf('\n') >= 0 || x.IndexOf('\0') >= 0)) return false;
            if (r[0] == "SKIP") return true;
            if (r[0] == "CMD") return BcdArgs.IsMatch(r[2]) && r[2].IndexOfAny(new[] { '&', '|', '<', '>', '^', '%', '"' }) < 0;
            return (r[1].StartsWith(@"HKEY_LOCAL_MACHINE\AT_SYSTEM\", StringComparison.Ordinal) || r[1].StartsWith(@"HKEY_LOCAL_MACHINE\AT_SOFTWARE\", StringComparison.Ordinal)
                || r[1].StartsWith(@"HKEY_USERS\AT_USER\", StringComparison.Ordinal)) && r[1].IndexOfAny(new[] { ']', '[' }) < 0 && RegValue.IsMatch(r[2]);
        }

        public static string Offline(Hive h, string path)
        {
            if (h == Hive.HKCU) return @"HKEY_USERS\AT_USER\" + path;
            const string CCS = @"SYSTEM\CurrentControlSet\";
            if (path.StartsWith(CCS, StringComparison.OrdinalIgnoreCase))
            {
                object cur = Reg.Get(Hive.HKLM, @"SYSTEM\Select", "Current");
                int n = cur is int ? (int)cur : 1;
                return @"HKEY_LOCAL_MACHINE\AT_SYSTEM\ControlSet" + n.ToString("000") + "\\" + path.Substring(CCS.Length);
            }
            if (path.StartsWith(@"SYSTEM\", StringComparison.OrdinalIgnoreCase)) return @"HKEY_LOCAL_MACHINE\AT_SYSTEM\" + path.Substring(7);
            if (path.StartsWith(@"SOFTWARE\", StringComparison.OrdinalIgnoreCase)) return @"HKEY_LOCAL_MACHINE\AT_SOFTWARE\" + path.Substring(9);
            return null;   // another hive: not reachable offline
        }

        static string Q(string s) { return RegText.Quote(s); }
        // one .reg value line that puts the value back exactly (or deletes it if it didn't exist); 3.32: RegText.Line
        public static string RegLine(string name, object v, RegistryValueKind k) { return RegText.Line(name, v, k); }

        static void Value(List<string[]> rows, string id, Hive h, string path, string name)
        {
            string key = Offline(h, path);
            if (key == null) { rows.Add(new[] { "SKIP", id, h + "\\" + path }); return; }
            string vn = (string.IsNullOrEmpty(name) ? "@" : Q(name)) + "=";
            if (rows.Any(r => r[1] == key && r[2].StartsWith(vn, StringComparison.Ordinal))) return;   // first capture = the original
            object v; RegistryValueKind jk;
            if (Journal.Original(h, path, name, out v, out jk)) { rows.Add(new[] { id, key, RegLine(name, v, jk) }); return; }   // 3.23: first original, also for tweaks applied earlier
            // 3.27.1: a backfill runs after the tweak is on, so the value now is the tweak's own value, not the original
            if (backfilling) { rows.Add(new[] { "SKIP", id, "no original known for " + h + "\\" + path + " " + name }); return; }
            v = Reg.Get(h, path, name);
            rows.Add(new[] { id, key, RegLine(name, v, v == null ? RegistryValueKind.Unknown : Reg.GetKind(h, path, name)) });
        }

        // bcdedit without an entry id changes the CURRENT entry; in Windows Recovery that is Recovery itself, so use {default}
        public static string ForRecovery(string bcdArgs)
        {
            var m = Regex.Match(bcdArgs.Trim(), @"^/(set|deletevalue)\s+(?!\{)", RegexOptions.IgnoreCase);
            return m.Success ? "/" + m.Groups[1].Value + " {default} " + bcdArgs.Trim().Substring(m.Length) : bcdArgs.Trim();
        }

        // call before applying a Danger zone tweak
        [ThreadStatic] static bool backfilling;
        // 3.27.1: false when the kit could not save the originals. backfill = the tweak is already applied.
        public static bool Capture(Tweak t) { return Capture(t, false); }
        public static bool Capture(Tweak t, bool backfill)
        {
            backfilling = backfill;
            try
            {
                var rows = Read();
                if (rows.Any(r => r[0] == t.Id || r[1] == t.Id)) return true;   // already in the kit: keep the first originals
                foreach (var o in t.Ops)
                {
                    var vo = o as VOp; if (vo != null) { Value(rows, t.Id, vo.H, vo.Path, vo.Name); continue; }
                    var ko = o as KeepOp; if (ko != null) { Value(rows, t.Id, ko.H, ko.Path, ko.Name); continue; }
                    var so = o as SvcOp; if (so != null) { SvcRows(rows, t.Id, so.Name); continue; }
                    var sp = o as SvcPrevOp; if (sp != null) { SvcRows(rows, t.Id, sp.Name); continue; }
                    var dr = o as DeviceRegOp; if (dr != null) { foreach (var k in dr.Targets()) Value(rows, t.Id, Hive.HKLM, k, dr.Name); continue; }
                    var dd = o as DevDisableOp;
                    if (dd != null)
                    {
                        List<DevInfo> devs;
                        try { devs = DevCtl.Find(dd.Ids, dd.Names); }
                        catch (Exception ex) { rows.Add(new[] { "SKIP", t.Id, "device list: " + ex.Message.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ') }); continue; }   // keep the rows already collected
                        foreach (var d in devs) Value(rows, t.Id, Hive.HKLM, @"SYSTEM\CurrentControlSet\Enum\" + d.Id, "ConfigFlags");
                        continue;
                    }
                    rows.Add(new[] { "SKIP", t.Id, o.GetType().Name });
                }
                for (int i = 0; i < t.ApplyCmds.Count; i++)
                {
                    bool bcd = t.ApplyCmds[i].Length > 0 && t.ApplyCmds[i][0].IndexOf("bcdedit", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!bcd) { rows.Add(new[] { "SKIP", t.Id, string.Join(" ", t.ApplyCmds[i]) }); continue; }
                    foreach (var r in t.RevertCmds.Where(c => c.Length > 1 && c[0].IndexOf("bcdedit", StringComparison.OrdinalIgnoreCase) >= 0))
                        if (!rows.Any(x => x[0] == "CMD" && x[1] == t.Id && x[2] == ForRecovery(r[1]))) rows.Add(new[] { "CMD", t.Id, ForRecovery(r[1]) });
                }
                // 3.26: a tweak with nothing to save on this PC (a missing service, no matching device) still gets a
                // marker, so the backfill does not add it again at every start
                if (!rows.Any(r => r[0] == t.Id || r[1] == t.Id)) rows.Add(new[] { "SKIP", t.Id, "nothing to save on this PC" });
                Write(rows);
                return true;
            }
            catch (Exception ex) { Log.W("Recovery kit: {0}", ex.Message); return false; }
            finally { backfilling = false; }
        }
        static void SvcRows(List<string[]> rows, string id, string name)
        {
            string p = @"SYSTEM\CurrentControlSet\Services\" + name;
            if (Reg.Get(Hive.HKLM, p, "Start") == null) return;
            Value(rows, id, Hive.HKLM, p, "Start");
            Value(rows, id, Hive.HKLM, p, "DelayedAutostart");
        }

        // call after a Danger zone tweak was undone normally
        public static void Release(string id)
        {
            try
            {
                if (!File.Exists(Ledger)) return;
                var rows = Read().Where(r => r[0] != id && r[1] != id).ToList();
                if (rows.Count(r => r[0] != "SKIP") == 0) { ClearAll(); return; }
                Write(rows);
            }
            catch (Exception ex) { Log.W("Recovery kit: {0}", ex.Message); }
        }
        public static void ClearAll()
        {
            try
            {
                if (!Directory.Exists(Folder)) return;
                if ((File.GetAttributes(Folder) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(Folder, false); Log.W("Recovery kit: removed a junction planted as {0}", Folder); return; }   // never follow it
                Directory.Delete(Folder, true);
            }
            catch (Exception ex) { Log.W("Recovery kit: {0}", ex.Message); }
        }

        public static IEnumerable<string> Covered() { return Read().Select(r => r[0] == "CMD" || r[0] == "SKIP" ? r[1] : r[0]).Distinct(); }

        static void Write(List<string[]> rows)
        {
            if (FolderOverride == null) Secure.LockDir(Folder); else Directory.CreateDirectory(Folder);
            rows = rows.Where(RowOk).ToList();
            File.WriteAllLines(Ledger, rows.Select(r => string.Join("\t", r)).ToArray(), new UTF8Encoding(false));
            var reg = new StringBuilder("Windows Registry Editor Version 5.00\r\n");
            foreach (var g in rows.Where(r => r[0] != "CMD" && r[0] != "SKIP").GroupBy(r => r[1]))
            {
                reg.Append("\r\n[").Append(g.Key).Append("]\r\n");
                foreach (var r in g) reg.Append(r[2]).Append("\r\n");
            }
            File.WriteAllText(Path.Combine(Folder, "undo.reg"), reg.ToString(), Encoding.Unicode);
            bool user = rows.Any(r => r[1].StartsWith(@"HKEY_USERS\AT_USER", StringComparison.Ordinal));
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string rel = profile.Length > 3 ? profile.Substring(3) : profile;
            var cmd = new StringBuilder();
            cmd.Append("@echo off\r\nrem ArchTweaker recovery: undoes the Danger zone tweaks listed in README.txt when Windows doesn't start.\r\nrem Run it from Windows Recovery > Troubleshoot > Advanced options > Command Prompt.\r\n");
            cmd.Append("setlocal\r\nset W=\r\nfor %%d in (C D E F G H I J K L M N O P Q R S T U V W Y Z) do if exist %%d:\\ArchTweaker-Recovery\\undo.reg if exist %%d:\\Windows\\System32\\config\\SYSTEM set W=%%d\r\n");
            cmd.Append("if \"%W%\"==\"\" (echo Windows with ArchTweaker-Recovery not found on any drive. & exit /b 1)\r\necho Windows is on %W%:\r\n");
            cmd.Append("reg load HKLM\\AT_SYSTEM %W%:\\Windows\\System32\\config\\SYSTEM\r\nreg load HKLM\\AT_SOFTWARE %W%:\\Windows\\System32\\config\\SOFTWARE\r\n");
            if (user) cmd.Append("reg load HKU\\AT_USER \"%W%:\\" + rel + "\\NTUSER.DAT\"\r\n");
            cmd.Append("reg import %W%:\\ArchTweaker-Recovery\\undo.reg\r\n");
            if (user) cmd.Append("reg unload HKU\\AT_USER\r\n");
            cmd.Append("reg unload HKLM\\AT_SOFTWARE\r\nreg unload HKLM\\AT_SYSTEM\r\n");
            foreach (var r in rows.Where(r => r[0] == "CMD")) cmd.Append("bcdedit ").Append(r[2]).Append("\r\n");
            cmd.Append("echo.\r\necho Done. Close this window and choose Continue to start Windows. Then open ArchTweaker and undo the tweak there too.\r\npause\r\n");
            File.WriteAllText(Path.Combine(Folder, "recovery.cmd"), cmd.ToString(), Encoding.ASCII);
            var ids = Covered().ToList();
            var readme = new StringBuilder();
            readme.Append("ArchTweaker recovery kit · ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("\r\n\r\n");
            readme.Append("If Windows doesn't start after a Danger zone tweak:\r\n");
            readme.Append(" 1. Let Windows fail to start twice (or hold Shift while choosing Restart) to reach Windows Recovery.\r\n");
            readme.Append(" 2. Troubleshoot > Advanced options > Command Prompt.\r\n");
            readme.Append(" 3. Find this folder:  for %d in (C D E F G) do @if exist %d:\\ArchTweaker-Recovery echo %d:\r\n");
            readme.Append(" 4. Run  X:\\ArchTweaker-Recovery\\recovery.cmd  (X = the letter shown), close the window, choose Continue.\r\n\r\n");
            readme.Append("It puts back the registry values, services, device states and boot settings these tweaks changed:\r\n");
            foreach (var id in ids) { var t = Catalog.All.FirstOrDefault(x => x.Id == id); readme.Append(" • ").Append(t != null ? t.Title : id).Append("\r\n"); }
            var skip = rows.Where(r => r[0] == "SKIP").ToList();
            if (skip.Count > 0)
            {
                readme.Append("\r\nNot covered here (they don't stop Windows from starting; undo them in ArchTweaker):\r\n");
                foreach (var r in skip) readme.Append(" • ").Append(r[1]).Append(": ").Append(r[2]).Append("\r\n");
            }
            readme.Append("\r\nArchTweaker deletes this folder when the last of these tweaks is undone.\r\n");
            readme.Append("\r\n— Русский —\r\nЕсли Windows не запускается после твика из опасной зоны: дважды дайте Windows не запуститься (или удерживайте Shift при выборе «Перезагрузка»), ");
            readme.Append("откройте «Поиск и устранение неисправностей > Дополнительные параметры > Командная строка», найдите эту папку командой выше и запустите recovery.cmd. Затем выберите «Продолжить».\r\n");
            File.WriteAllText(Path.Combine(Folder, "README.txt"), readme.ToString(), new UTF8Encoding(true));
        }
    }

    // 3.8.2: writes a registry value with the backup/restore privilege (SeRestorePrivilege).
    // Used only after a normal write is denied: some keys (for example WMI\Autologger\NetCore)
    // don't let administrators write, but every administrator has this privilege.
    public static class RegRestore
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegCreateKeyEx(UIntPtr hKey, string subKey, int reserved, string cls, uint options, int sam,
            IntPtr security, out IntPtr result, out int disposition);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegSetValueEx(IntPtr hKey, string name, int reserved, int type, byte[] data, int size);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegDeleteValue(IntPtr hKey, string name);
        [DllImport("advapi32.dll")] static extern int RegCloseKey(IntPtr hKey);
        const uint REG_OPTION_BACKUP_RESTORE = 4;
        const int KEY_WRITE_64 = 0x20006 | 0x0100, ERROR_FILE_NOT_FOUND = 2;
        static bool privs;

        static UIntPtr Root(Hive h)
        {
            switch (h)
            {
                case Hive.HKLM: return (UIntPtr)0x80000002u;
                case Hive.HKCU: return (UIntPtr)0x80000001u;
                case Hive.HKCR: return (UIntPtr)0x80000000u;
                default: return (UIntPtr)0x80000003u;
            }
        }

        static IntPtr Open(Hive h, string path)
        {
            if (!privs) { Privilege.Enable("SeRestorePrivilege"); Privilege.Enable("SeBackupPrivilege"); privs = true; }
            IntPtr k; int disp;
            int rc = RegCreateKeyEx(Root(h), path, 0, null, REG_OPTION_BACKUP_RESTORE, KEY_WRITE_64, IntPtr.Zero, out k, out disp);
            if (rc != 0) throw new UnauthorizedAccessException("Access to the registry key '" + Reg.HiveName(h) + "\\" + path + "' is denied, also with the backup/restore privilege (error " + rc + ").");
            return k;
        }

        // Converts a .NET registry value to the bytes RegSetValueEx needs.
        public static byte[] Bytes(RegistryValueKind kind, object value, out int type)
        {
            switch (kind)
            {
                case RegistryValueKind.DWord: type = 4; return BitConverter.GetBytes(value is uint ? unchecked((int)(uint)value) : Convert.ToInt32(value, CultureInfo.InvariantCulture));
                case RegistryValueKind.QWord: type = 11; return BitConverter.GetBytes(value is ulong ? unchecked((long)(ulong)value) : Convert.ToInt64(value, CultureInfo.InvariantCulture));
                case RegistryValueKind.ExpandString: type = 2; return Encoding.Unicode.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) + "\0");
                case RegistryValueKind.MultiString: type = 7; return Encoding.Unicode.GetBytes(string.Join("\0", (string[])value) + "\0\0");
                case RegistryValueKind.Binary: type = 3; return (byte[])value;
                default: type = 1; return Encoding.Unicode.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) + "\0");
            }
        }

        public static void Set(Hive h, string path, string name, RegistryValueKind kind, object value)
        {
            int type; byte[] data = Bytes(kind, value, out type);
            IntPtr k = Open(h, path);
            try
            {
                int rc = RegSetValueEx(k, name, 0, type, data, data.Length);
                if (rc != 0) throw new UnauthorizedAccessException("Couldn't write " + path + "\\" + name + " (error " + rc + ").");
                Log.W("  {0}\\{1}: written with the backup/restore privilege (the key denies administrators)", path, name);
            }
            finally { RegCloseKey(k); }
        }

        public static void Delete(Hive h, string path, string name)
        {
            IntPtr k = Open(h, path);
            try
            {
                int rc = RegDeleteValue(k, name);
                if (rc != 0 && rc != ERROR_FILE_NOT_FOUND) throw new UnauthorizedAccessException("Couldn't delete " + path + "\\" + name + " (error " + rc + ").");
            }
            finally { RegCloseKey(k); }
        }
    }
}
