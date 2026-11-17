// ArchTweaker: The filterable log store behind Show log.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ================= LOG STORE =================
    // Every log line of this session, kept outside the window so a rebuild (theme, language, DPI) keeps the whole
    // log and the filter can be changed after the fact. Lines from Log.Say are "results"; problems are matched by wording.
    public static class LogStore
    {
        public enum Kind { Detail, Result, Problem }
        public struct Entry { public string Text; public Kind Kind; }
        static readonly List<Entry> lines = new List<Entry>();
        static readonly object gate = new object();
        public static readonly string[] FilterNames = { "All messages", "Results only", "Problems only" };
        public static int Filter;     // per session: the full log is the safer default when something goes wrong
        static event Action<Entry> Added;
        // snapshot of the log so far and live updates from then on, without a gap or a repeat
        public static string Subscribe(Action<Entry> h, int filter) { lock (gate) { Added += h; return TextLocked(filter); } }
        public static void Unsubscribe(Action<Entry> h) { lock (gate) Added -= h; }
        static bool hooked;
        static readonly Regex Bad = new Regex(@"\b(FAILED|failed|Error|error|couldn't|could not|can't|not found|denied|refused|timed out|Warning)\b", RegexOptions.Compiled);

        public static void Hook()
        {
            if (hooked) return; hooked = true;
            Log.Line += s =>
            {
                var e = new Entry { Text = s, Kind = Bad.IsMatch(s) ? Kind.Problem : Kind.Detail };
                lock (gate)
                {
                    lines.Add(e); if (lines.Count > 6000) lines.RemoveRange(0, 1000);
                    var h = Added; if (h != null) h(e);   // handlers only queue a BeginInvoke
                }
            };
            // Log.Say writes the line first (Line), then raises Notice: mark that line a result
            Log.Notice += s =>
            {
                lock (gate)
                    for (int i = lines.Count - 1; i >= 0 && i >= lines.Count - 3; i--)
                        if (lines[i].Text.EndsWith(s, StringComparison.Ordinal))
                        {
                            var e = lines[i];
                            if (e.Kind == Kind.Detail) { e.Kind = Kind.Result; lines[i] = e; var h = Added; if (h != null) h(e); }   // "results only" shows it now
                            break;
                        }
            };
        }
        public static bool Pass(Entry e, int filter)
        {
            if (filter == 1) return e.Kind != Kind.Detail;
            if (filter == 2) return e.Kind == Kind.Problem;
            return true;
        }
        public static string Text(int filter) { lock (gate) return TextLocked(filter); }
        static string TextLocked(int filter)
        {
            var sel = lines.Where(e => Pass(e, filter)).Select(e => e.Text).ToArray();
            return sel.Length == 0 ? "" : string.Join(Environment.NewLine, sel) + Environment.NewLine;
        }
        // what a person asking for help should paste: version, Windows build, then the filtered log
        public static string Support(int filter, int pending)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ArchTweaker " + Program.Version + " · " + OSInfo.Name + " (build " + OSInfo.Build + ")");
            sb.AppendLine("Copied " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " · log filter: " + FilterNames[filter] + " · changes waiting to apply: " + pending);
            sb.AppendLine(new string('-', 60));
            List<Entry> copy; lock (gate) copy = lines.ToList();
            var sel = copy.Where(e => Pass(e, filter)).Select(e => e.Text).ToList();
            if (sel.Count > 800) { sb.AppendLine("(first " + (sel.Count - 800) + " lines left out)"); sel = sel.Skip(sel.Count - 800).ToList(); }
            foreach (var l in sel) sb.AppendLine(l);
            return sb.ToString();
        }
    }
}
