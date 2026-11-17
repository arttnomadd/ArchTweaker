// ArchTweaker: System snapshots and their diff (What changed).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ================= SNAPSHOT DIFF =================
    public class SnapDiff
    {
        public string Kind;      // "Tweak", "Service", "Startup"
        public string Name, Before, After;
        public Tweak Tweak; public string Service; public int ServiceStart = -1; public bool ServiceDelayed;
        public bool CanFix { get { return Tweak != null || Service != null; } }
    }

    public static class Snapshot
    {
        static string File_ { get { return Path.Combine(Paths.Root, "snapshot.txt"); } }
        const string SVC = @"SYSTEM\CurrentControlSet\Services";
        static readonly string[] RunKeys = { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run" };

        public static bool Exists { get { return File.Exists(File_); } }
        public static DateTime When { get { try { return File.GetLastWriteTime(File_); } catch { return DateTime.MinValue; } } }

        static Dictionary<string, string> Current()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in Reg.SubKeys(Hive.HKLM, SVC))
            {
                object st = Reg.Get(Hive.HKLM, SVC + "\\" + s, "Start"), type = Reg.Get(Hive.HKLM, SVC + "\\" + s, "Type");
                if (!(st is int) || !(type is int) || ((int)type & 0x30) == 0) continue;   // Win32 services only, not drivers
                object del = Reg.Get(Hive.HKLM, SVC + "\\" + s, "DelayedAutostart");
                d["svc\t" + s] = (int)st + (del is int && (int)del == 1 ? "d" : "");
            }
            for (int i = 0; i < RunKeys.Length; i++)
                foreach (var n in Reg.ValueNames(Hive.HKLM, RunKeys[i])) d["run\tHKLM" + (i == 1 ? "32" : "") + "\t" + n] = (Reg.Get(Hive.HKLM, RunKeys[i], n) ?? "").ToString();
            foreach (var n in Reg.ValueNames(Hive.HKCU, RunKeys[0])) d["run\tHKCU\t" + n] = (Reg.Get(Hive.HKCU, RunKeys[0], n) ?? "").ToString();
            return d;
        }

        public static void Capture()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var kv in Current()) sb.Append(kv.Key).Append('\t').Append(kv.Value.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ")).Append('\n');
                File.WriteAllText(File_, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { Log.W("Snapshot: {0}", ex.Message); }
        }

        static Dictionary<string, string> Saved()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Secure.DropUntrusted(File_, "the 'What changed' snapshot")) return d;
            foreach (var line in File.ReadAllLines(File_))
            {
                int last = line.LastIndexOf('\t'); if (last <= 0) continue;
                d[line.Substring(0, last)] = line.Substring(last + 1);
            }
            return d;
        }

        // Tweaks reset since they were applied, services whose start type changed, startup entries added or removed.
        public static List<SnapDiff> Diff()
        {
            var l = new List<SnapDiff>();
            foreach (var d in Drift.Against(AppliedSet.Read()))
                l.Add(new SnapDiff { Kind = "Tweak", Name = d.Tweak.Title, Before = "Applied", After = "Reset", Tweak = d.Tweak });
            if (!Exists) return l;
            Dictionary<string, string> was, now = Current();
            try { was = Saved(); } catch { return l; }
            foreach (var kv in was)
            {
                string cur; bool has = now.TryGetValue(kv.Key, out cur);
                var p = kv.Key.Split(new[] { '\t' });
                if (p[0] == "svc")
                {
                    if (!has || cur == kv.Value) continue;
                    int st; bool delayed = kv.Value.EndsWith("d");
                    if (!int.TryParse(kv.Value.TrimEnd(new[] { 'd' }), out st)) continue;
                    l.Add(new SnapDiff { Kind = "Service", Name = p[1], Before = SvcWord(kv.Value), After = SvcWord(cur), Service = p[1], ServiceStart = st, ServiceDelayed = delayed });
                }
                else if (p[0] == "run" && p.Length > 2 && !has)
                    l.Add(new SnapDiff { Kind = "Startup", Name = p[2] + "  (" + p[1] + ")", Before = "Present", After = "Removed" });
            }
            foreach (var kv in now)
            {
                var p = kv.Key.Split(new[] { '\t' });
                if (p[0] == "run" && p.Length > 2 && !was.ContainsKey(kv.Key))
                    l.Add(new SnapDiff { Kind = "Startup", Name = p[2] + "  (" + p[1] + ")", Before = "—", After = "Added: " + kv.Value });
            }
            return l;
        }

        static string SvcWord(string v)
        {
            int st; bool del = v.EndsWith("d");
            if (!int.TryParse(v.TrimEnd(new[] { 'd' }), out st)) return v;
            return Svc.StartName(st, del);
        }

        public static bool Fix(SnapDiff d)
        {
            try
            {
                if (d.Tweak != null) { d.Tweak.Apply(); AppliedSet.Mark(d.Tweak, true); return true; }
                if (d.Service != null) { Svc.SetStart(d.Service, d.ServiceStart, d.ServiceDelayed); return true; }
            }
            catch (Exception ex) { Log.W("Fix {0}: {1}", d.Name, ex.Message); }
            return false;
        }
    }
}
