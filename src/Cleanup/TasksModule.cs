// ArchTweaker: Scheduled tasks: list, enable / disable with exact undo, run now (3.39).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ArchTweaker
{
    public class SchedTask
    {
        public string Path = "", Name = "", State = "", Author = "";
        public string Full { get { return Path + Name; } }
        public bool Enabled { get { return !State.Equals("Disabled", StringComparison.OrdinalIgnoreCase); } }
        public bool Microsoft { get { return TasksModule.IsMicrosoft(Path, Author); } }
        public bool Own { get { return TasksModule.IsOwn(Path, Name); } }
    }

    // Every change made from the Scheduled tasks page is journaled once, with the state the task had before
    // ArchTweaker first touched it, so "Put back all" and "Undo everything" restore it exactly. Tasks are never deleted.
    public static class TasksModule
    {
        public static string JournalOverride;   // tests
        static string JournalFile { get { return JournalOverride ?? System.IO.Path.Combine(Paths.Root, "tasks-changed.tsv"); } }
        // tests swap this; true = the change worked
        internal static Func<string, bool, bool> Setter = SetWithPowerShell;

        public static List<SchedTask> Load()
        {
            return Parse(Shell.PS("Get-ScheduledTask | ForEach-Object { $_.TaskPath + '|' + $_.TaskName + '|' + $_.State + '|' + $_.Author }") ?? "");
        }

        // "TaskPath|TaskName|State|Author" per line; third-party tasks first, then by folder and name
        public static List<SchedTask> Parse(string text)
        {
            var l = new List<SchedTask>();
            foreach (var raw in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = raw.Split(new[] { '|' }, 4);
                if (p.Length < 3 || p[1].Trim().Length == 0 || !p[0].Trim().StartsWith("\\", StringComparison.Ordinal)) continue;
                l.Add(new SchedTask { Path = p[0].Trim(), Name = p[1].Trim(), State = p[2].Trim(), Author = p.Length > 3 ? p[3].Trim() : "" });
            }
            return l.OrderBy(t => t.Microsoft ? 1 : 0).ThenBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ArchTweaker's own tasks (boot guard, maintenance, timer service): managed on Tools & backup, never switched here
        public static bool IsOwn(string path, string name)
        {
            return (path ?? "").StartsWith(@"\ArchTweaker\", StringComparison.OrdinalIgnoreCase)
                || (name ?? "").StartsWith("ArchTweaker", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsMicrosoft(string path, string author)
        {
            return (path ?? "").StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)
                || (author ?? "").IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // a full task path goes into a PowerShell string: it starts at the root and has no line break or NUL
        public static bool SafeName(string full)
        {
            return !string.IsNullOrEmpty(full) && full.StartsWith("\\", StringComparison.Ordinal) && full.Length > 1 && full.IndexOfAny(new[] { '\r', '\n', '\0' }) < 0;
        }

        // PowerShell single-quoted literal: ' doubles, nothing else is special
        public static string PsQuote(string s) { return "'" + (s ?? "").Replace("'", "''") + "'"; }

        // "\Folder\Sub\Name" → ("\Folder\Sub\", "Name")
        public static void Split(string full, out string path, out string name)
        {
            int i = full.LastIndexOf('\\');
            path = full.Substring(0, i + 1); name = full.Substring(i + 1);
        }

        // Turns a task on or off. The state before ArchTweaker's first change is journaled first.
        public static bool SetEnabled(SchedTask t, bool on)
        {
            if (t == null || !SafeName(t.Full) || t.Own) return false;
            if (t.Enabled == on) return true;
            Remember(t.Full, t.Enabled);
            bool ok = Setter(t.Full, on);
            if (ok) t.State = on ? "Ready" : "Disabled";
            Log.W("Scheduled task {0}: {1}{2}", t.Full, on ? "enabled" : "disabled", ok ? "" : " (failed)");
            return ok;
        }

        public static void Run(SchedTask t)
        {
            if (t == null || !SafeName(t.Full)) return;
            string p, n; Split(t.Full, out p, out n);
            Shell.PS("Start-ScheduledTask -TaskPath " + PsQuote(p) + " -TaskName " + PsQuote(n) + " -ErrorAction SilentlyContinue");
            Log.W("Scheduled task {0}: started", t.Full);
        }

        // OK / FAIL from PowerShell: schtasks prints localised text, so its output can't be checked
        static bool SetWithPowerShell(string full, bool on)
        {
            if (!SafeName(full)) return false;
            string p, n; Split(full, out p, out n);
            string o = Shell.PS("try { " + (on ? "Enable" : "Disable") + "-ScheduledTask -TaskPath " + PsQuote(p) + " -TaskName " + PsQuote(n) + " -ErrorAction Stop | Out-Null; 'OK' } catch { 'FAIL' }") ?? "";
            return o.Trim().EndsWith("OK", StringComparison.Ordinal);
        }

        // ---------- journal: full task path TAB 1|0 (enabled before ArchTweaker's first change) ----------
        public static Dictionary<string, bool> Journal()
        {
            var d = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(JournalFile)) return d;
                foreach (var line in File.ReadAllLines(JournalFile))
                {
                    var p = line.Split('\t');
                    if (p.Length == 2 && SafeName(p[0]) && (p[1] == "1" || p[1] == "0") && !d.ContainsKey(p[0])) d[p[0]] = p[1] == "1";
                }
            }
            catch (Exception ex) { Log.W("Scheduled tasks journal: {0}", ex.Message); }
            return d;
        }

        static void Remember(string full, bool wasEnabled)
        {
            if (Journal().ContainsKey(full)) return;   // the first state wins
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(JournalFile));
                File.AppendAllText(JournalFile, full + "\t" + (wasEnabled ? "1" : "0") + Environment.NewLine);
            }
            catch (Exception ex) { Log.W("Scheduled tasks journal: {0}", ex.Message); }
        }

        // Puts every task changed from the page back to its first-seen state. Returns how many were restored.
        public static int RestoreAll()
        {
            var j = Journal();
            int n = 0; var failed = new List<string>();
            foreach (var kv in j)
            {
                if (Setter(kv.Key, kv.Value)) n++; else failed.Add(kv.Key);
            }
            try
            {
                if (failed.Count == 0) { if (File.Exists(JournalFile)) File.Delete(JournalFile); }
                else File.WriteAllLines(JournalFile, failed.Select(f => f + "\t" + (j[f] ? "1" : "0")).ToArray());   // keep what didn't go back
            }
            catch (Exception ex) { Log.W("Scheduled tasks journal: {0}", ex.Message); }
            if (j.Count > 0) Log.W("Scheduled tasks: {0} put back{1}", n, failed.Count > 0 ? ", " + failed.Count + " failed" : "");
            return n;
        }
    }
}
