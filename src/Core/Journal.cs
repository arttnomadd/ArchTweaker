// ArchTweaker: undo journal and state (3.32 split from Engine.cs).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static class Journal
    {
        static HashSet<string> seen;
        static readonly object sync = new object();

        // 3.2.1: a journal not owned by an administrator may have been planted: set it aside, never replay it
        static void CheckTrust()
        {
            string j = Paths.Journal;
            if (!File.Exists(j) || Secure.TrustedData(j, "the undo journal")) return;
            try { File.Move(j, j + ".untrusted-" + DateTime.Now.ToString("yyyyMMddHHmmss")); } catch { try { File.Delete(j); } catch { } }
        }

        static void Load()
        {
            if (seen != null) return;
            seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CheckTrust();
            if (!File.Exists(Paths.Journal)) return;
            foreach (var line in File.ReadAllLines(Paths.Journal))
            {
                var p = line.Split('\t');
                if (p.Length >= 4) seen.Add(p[0] + "|" + p[1] + "|" + p[2] + "|" + p[3]);
            }
        }

        static void Append(string type, Hive h, string path, string name, string extra)
        {
            lock (sync)
            {
                Load();
                string key = type + "|" + h + "|" + path + "|" + name;
                if (type == "V" && seen.Contains(key)) return;
                if (type == "KC" && seen.Contains(key)) return;
                seen.Add(key);
                File.AppendAllText(Paths.Journal, type + "\t" + h + "\t" + path + "\t" + name + "\t" + extra + Environment.NewLine);
            }
        }

        public static void RecordValue(Hive h, string path, string name)
        {
            object cur = Reg.Get(h, path, name);
            string extra;
            if (cur == null) extra = "0";
            else
            {
                RegistryValueKind k = Reg.GetKind(h, path, name);
                extra = "1\t" + k + "\t" + Serialize(cur);
            }
            Append("V", h, path, name ?? "", extra);
        }

        public static void RecordKeyCreated(Hive h, string path) { Append("KC", h, path, "", ""); }
        public static void RecordKeyDeleted(Hive h, string path, string file) { Append("KD", h, path, "", file); }

        static string Serialize(object o)
        {
            byte[] data;
            if (o is byte[]) data = (byte[])o;
            else if (o is string[]) data = Encoding.UTF8.GetBytes(string.Join("\0", (string[])o));
            else if (o is int) data = Encoding.UTF8.GetBytes(((int)o).ToString());
            else if (o is long) data = Encoding.UTF8.GetBytes(((long)o).ToString());
            else data = Encoding.UTF8.GetBytes(o.ToString());
            return Convert.ToBase64String(data);
        }

        static object Deserialize(RegistryValueKind k, string b64)
        {
            byte[] d = Convert.FromBase64String(b64);
            switch (k)
            {
                case RegistryValueKind.Binary: return d;
                case RegistryValueKind.MultiString: return Encoding.UTF8.GetString(d).Split('\0');
                case RegistryValueKind.DWord: return int.Parse(Encoding.UTF8.GetString(d));
                case RegistryValueKind.QWord: return long.Parse(Encoding.UTF8.GetString(d));
                default: return Encoding.UTF8.GetString(d);
            }
        }

        // 3.23: the value from before ArchTweaker first changed it. False when the journal has no entry; v = null when it did not exist.
        public static bool Original(Hive h, string path, string name, out object v, out RegistryValueKind k)
        {
            v = null; k = RegistryValueKind.Unknown;
            try
            {
                CheckTrust();
                if (!File.Exists(Paths.Journal)) return false;
                string hs = h.ToString();
                foreach (var line in File.ReadLines(Paths.Journal))
                {
                    var p = line.Split('\t');
                    if (p.Length < 5 || p[0] != "V" || p[1] != hs || !string.Equals(p[2], path, StringComparison.OrdinalIgnoreCase) || !string.Equals(p[3], name ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                    if (p[4] == "0") return true;
                    if (p.Length < 7) return false;
                    k = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), p[5]); v = Deserialize(k, p[6]);
                    return true;
                }
            }
            catch (Exception ex) { Log.W("Undo journal: could not read the original of {0}\\{1}\\{2}: {3}", h, path, name, ex.Message); }
            return false;
        }

        public static int Count
        {
            get { CheckTrust(); return File.Exists(Paths.Journal) ? File.ReadAllLines(Paths.Journal).Length : 0; }
        }

        public static void UndoAll()
        {
            StateCache.Clear();   // changes outside the tweak catalog make cached states stale
            CheckTrust();
            if (!File.Exists(Paths.Journal)) { Log.W("Nothing to undo."); return; }
            var lines = File.ReadAllLines(Paths.Journal).Reverse().ToList();
            int ok = 0, fail = 0;
            foreach (var line in lines)
            {
                var p = line.Split('\t');
                if (p.Length < 4) continue;
                try
                {
                    Hive h = (Hive)Enum.Parse(typeof(Hive), p[1]);
                    string path = p[2], name = p[3];
                    if (p[0] == "V")
                    {
                        if (p[4] == "0") Reg.DeleteValueRaw(h, path, name);
                        else
                        {
                            var kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), p[5]);
                            Reg.SetRaw(h, path, name, kind, Deserialize(kind, p[6]));
                        }
                    }
                    else if (p[0] == "KC") Reg.DeleteTreeRaw(h, path);
                    else if (p[0] == "KD" && Backups.Ok(p[4])) Shell.Run("reg.exe", "import \"" + p[4] + "\" /reg:64", true);
                    ok++;
                }
                catch (Exception ex) { fail++; Log.W("Undo failed: {0} ({1})", line, ex.Message); }
            }
            string done = Paths.Journal + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".undone";
            File.Move(Paths.Journal, done);
            lock (sync) seen = null;
            State.ClearAll();
            Log.W("Undo complete: {0} entries restored, {1} failed. Reboot recommended.", ok, fail);
            RecoveryKit.ClearAll();   // 3.8: nothing left for the recovery kit to undo
        }
    }

    // Remembers command-based tweaks that can't be detected from the registry.
    public static class State
    {
        const string P = @"Software\ArchTweaker\State";
        public static bool Get(string id)
        {
            object o = Reg.Get(Hive.HKCU, P, id);
            return o is int && (int)o == 1;
        }
        public static void Set(string id, bool on)
        {
            using (var b = Reg.Base(Hive.HKCU))
            using (var k = b.CreateSubKey(P)) k.SetValue(id, on ? 1 : 0, RegistryValueKind.DWord);
        }
        public static void ClearAll() { Reg.DeleteTreeRaw(Hive.HKCU, P); }
    }
}
