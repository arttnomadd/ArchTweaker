// ArchTweaker: Compares two power plans setting by setting.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ---------------- compare a .pow with the active plan ----------------
    public static class PlanCompare
    {
        // powercfg /qh output is localised; only GUIDs, indentation and 0x… values are read. In each setting block the
        // last two hex values are the AC and DC indexes (range settings list min / max / step before them).
        static readonly Regex Guid_ = new Regex(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);
        static readonly Regex Hex = new Regex(@"0x[0-9a-f]{8}", RegexOptions.IgnoreCase);

        public class Setting { public string Sub, Guid, Name; public uint Ac, Dc; }

        public static Dictionary<string, Setting> Parse(string text)
        {
            var map = new Dictionary<string, Setting>(StringComparer.OrdinalIgnoreCase);
            string sub = null; Setting cur = null; var hex = new List<uint>();
            Action flush = delegate { if (cur != null && hex.Count >= 2) { cur.Ac = hex[hex.Count - 2]; cur.Dc = hex[hex.Count - 1]; map[cur.Sub + "/" + cur.Guid] = cur; } cur = null; hex.Clear(); };
            foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                var g = Guid_.Match(line);
                if (g.Success)
                {
                    // scheme line at column 0, subgroups indented 2, settings indented 4
                    int indent = raw.Length - raw.TrimStart().Length;
                    flush();
                    if (indent == 0) continue;
                    if (indent <= 2) { sub = g.Value.ToLowerInvariant(); continue; }
                    int p = line.IndexOf('(');
                    cur = new Setting { Sub = sub ?? "", Guid = g.Value.ToLowerInvariant(), Name = p >= 0 ? line.Substring(p).Trim('(', ')', ' ') : g.Value };
                    continue;
                }
                if (cur != null) foreach (Match m in Hex.Matches(line)) hex.Add(Convert.ToUInt32(m.Value.Substring(2), 16));
            }
            flush();
            return map;
        }

        public static string Diff(Dictionary<string, Setting> active, Dictionary<string, Setting> file, string fileName)
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var kv in file.OrderBy(k => k.Value.Name))
            {
                Setting a;
                if (!active.TryGetValue(kv.Key, out a)) { sb.AppendLine("+ " + kv.Value.Name + ": only in " + fileName); n++; continue; }
                if (a.Ac != kv.Value.Ac || a.Dc != kv.Value.Dc)
                {
                    sb.AppendFormat("• {0}: plugged in {1} → {2}, battery {3} → {4}\r\n", kv.Value.Name, a.Ac, kv.Value.Ac, a.Dc, kv.Value.Dc);
                    n++;
                }
            }
            return (n == 0 ? "No differences: the file matches the active plan." : n + " setting(s) differ (active → file):") + "\r\n\r\n" + sb.ToString();
        }

        public static string Run(string file)
        {
            string g = Guid.NewGuid().ToString();
            Shell.Run("powercfg.exe", "/import \"" + file + "\" " + g, true);
            try
            {
                if (Shell.Run("powercfg.exe", "/list", true).IndexOf(g, StringComparison.OrdinalIgnoreCase) < 0) return "Could not read " + Path.GetFileName(file) + " (powercfg /import failed).";
                var f = Parse(Shell.Run("powercfg.exe", "/qh " + g, true));
                var a = Parse(Shell.Run("powercfg.exe", "/qh SCHEME_CURRENT", true));
                return Diff(a, f, Path.GetFileName(file));
            }
            finally { Shell.Run("powercfg.exe", "/delete " + g, true); }
        }
    }
}
