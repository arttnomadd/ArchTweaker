// ArchTweaker: registry access and journaled writes (3.32 split from Engine.cs).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static class Reg
    {
        public static RegistryKey Base(Hive h)
        {
            RegistryView v = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default;
            switch (h)
            {
                case Hive.HKLM: return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, v);
                case Hive.HKCU: return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, v);
                case Hive.HKCR: return RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, v);
                default: return RegistryKey.OpenBaseKey(RegistryHive.Users, v);
            }
        }

        public static string HiveName(Hive h)
        {
            switch (h)
            {
                case Hive.HKLM: return "HKEY_LOCAL_MACHINE";
                case Hive.HKCU: return "HKEY_CURRENT_USER";
                case Hive.HKCR: return "HKEY_CLASSES_ROOT";
                default: return "HKEY_USERS";
            }
        }

        public static object Get(Hive h, string path, string name)
        {
            try
            {
                using (var b = Base(h))
                using (var k = b.OpenSubKey(path, false))
                {
                    if (k == null) return null;
                    return k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                }
            }
            catch { return null; }
        }

        public static RegistryValueKind GetKind(Hive h, string path, string name)
        {
            try
            {
                using (var b = Base(h))
                using (var k = b.OpenSubKey(path, false))
                {
                    if (k == null) return RegistryValueKind.Unknown;
                    return k.GetValueKind(name);
                }
            }
            catch { return RegistryValueKind.Unknown; }
        }

        public static bool KeyExists(Hive h, string path)
        {
            try
            {
                using (var b = Base(h))
                using (var k = b.OpenSubKey(path, false))
                    return k != null;
            }
            catch { return false; }
        }

        public static string[] SubKeys(Hive h, string path)
        {
            try
            {
                using (var b = Base(h))
                using (var k = b.OpenSubKey(path, false))
                    return k == null ? new string[0] : k.GetSubKeyNames();
            }
            catch { return new string[0]; }
        }

        public static string[] ValueNames(Hive h, string path)
        {
            try
            {
                using (var b = Base(h))
                using (var k = b.OpenSubKey(path, false))
                    return k == null ? new string[0] : k.GetValueNames();
            }
            catch { return new string[0]; }
        }

        // Records the first missing ancestor key so undo can remove keys we created.
        static void JournalKeyCreation(Hive h, string path)
        {
            string[] parts = path.Split('\\');
            string cur = "";
            for (int i = 0; i < parts.Length; i++)
            {
                cur = i == 0 ? parts[0] : cur + "\\" + parts[i];
                if (!KeyExists(h, cur)) { Journal.RecordKeyCreated(h, cur); return; }
            }
        }

        public static void Set(Hive h, string path, string name, RegistryValueKind kind, object value)
        {
            JournalKeyCreation(h, path);
            Journal.RecordValue(h, path, name);
            SetRaw(h, path, name, kind, value);
        }

        // Writes a value without journaling. When the key's permissions deny administrators
        // (some Autologger and service keys), retries with the backup/restore privilege.
        public static void SetRaw(Hive h, string path, string name, RegistryValueKind kind, object value)
        {
            try
            {
                using (var b = Base(h))
                using (var k = b.CreateSubKey(path))
                {
                    if (k == null) throw new Exception("Cannot open " + path);
                    k.SetValue(name, value, kind);
                }
            }
            catch (UnauthorizedAccessException) { RegRestore.Set(h, path, name, kind, value); }
            catch (System.Security.SecurityException) { RegRestore.Set(h, path, name, kind, value); }
        }

        // Deletes a value without journaling, with the same fallback as SetRaw.
        public static void DeleteValueRaw(Hive h, string path, string name)
        {
            try
            {
                using (var b = Base(h))
                using (var k = b.OpenSubKey(path, true))
                {
                    if (k != null) k.DeleteValue(name, false);
                }
            }
            catch (UnauthorizedAccessException) { RegRestore.Delete(h, path, name); }
            catch (System.Security.SecurityException) { RegRestore.Delete(h, path, name); }
        }

        public static void DeleteValue(Hive h, string path, string name)
        {
            if (Get(h, path, name) == null && GetKind(h, path, name) == RegistryValueKind.Unknown) return;
            Journal.RecordValue(h, path, name);
            DeleteValueRaw(h, path, name);
        }

        public static void EnsureKey(Hive h, string path)
        {
            JournalKeyCreation(h, path);
            using (var b = Base(h)) using (var k = b.CreateSubKey(path)) { }
        }

        // Deletes a tree without journaling (used by undo / reverting keys we own).
        public static void DeleteTreeRaw(Hive h, string path)
        {
            try
            {
                using (var b = Base(h)) b.DeleteSubKeyTree(path, false);
            }
            catch (Exception ex) { Log.W("Delete key failed {0}: {1}", path, ex.Message); }
        }

        // Deletes a tree after exporting it to a .reg backup (journaled, restorable).
        public static void DeleteTreeBackedUp(Hive h, string path)
        {
            if (!KeyExists(h, path)) return;
            string file = Path.Combine(Paths.Backups, Sanitize(path) + "_" + DateTime.Now.Ticks + ".reg");
            Shell.Run("reg.exe", "export \"" + HiveName(h) + "\\" + path + "\" \"" + file + "\" /y /reg:64", true);
            // Only delete if the backup actually got written — otherwise this would be an UNDOABLE delete.
            if (!File.Exists(file) || new FileInfo(file).Length < 8)
                throw new IOException("Registry backup export failed for " + HiveName(h) + "\\" + path + " — key NOT deleted (would be irreversible).");
            Journal.RecordKeyDeleted(h, path, file);
            DeleteTreeRaw(h, path);
        }

        static string Sanitize(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            string r = sb.ToString();
            return r.Length > 80 ? r.Substring(r.Length - 80) : r;
        }

        public static string Norm(object o)
        {
            if (o == null) return null;
            if (o is int) return ((uint)(int)o).ToString();
            if (o is uint) return ((uint)o).ToString();
            if (o is long) return ((ulong)(long)o).ToString();
            if (o is byte[]) return BitConverter.ToString((byte[])o);
            if (o is string[]) return string.Join("\n", (string[])o);
            return o.ToString();
        }
    }

    // Persistent journal of original values, so "Undo everything" can return the PC to its pre-ArchTweaker state.
}
