// ArchTweaker: Hosts file: block lists, allow lists, write and restore.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ---------------- HOSTS BLOCKER ----------------
    public static class HostsModule
    {
        const string BEGIN = "# >>> ArchTweaker telemetry block >>>", END = "# <<< ArchTweaker telemetry block <<<";
        public static string File_ { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts"); } }

        public static readonly string[] Default =
        {
            "vortex.data.microsoft.com", "vortex-win.data.microsoft.com", "settings-win.data.microsoft.com",
            "v10.events.data.microsoft.com", "v20.events.data.microsoft.com", "self.events.data.microsoft.com",
            "umwatson.events.data.microsoft.com", "watson.telemetry.microsoft.com", "watson.microsoft.com",
            "oca.telemetry.microsoft.com", "sqm.telemetry.microsoft.com", "telecommand.telemetry.microsoft.com",
            "telemetry.microsoft.com", "telemetry.appex.bing.net", "df.telemetry.microsoft.com",
            "reports.wes.df.telemetry.microsoft.com", "services.wes.df.telemetry.microsoft.com",
            "statsfe2.ws.microsoft.com", "browser.events.data.msn.com", "telemetry.urs.microsoft.com",
        };

        public static bool IsActive() { try { return File.ReadAllText(File_).Contains(BEGIN); } catch { return false; } }

        public static List<string> Current()
        {
            var l = new List<string>(); bool inside = false;
            try
            {
                foreach (var line in File.ReadAllLines(File_))
                {
                    if (line.Trim() == BEGIN) { inside = true; continue; }
                    if (line.Trim() == END) { inside = false; continue; }
                    if (inside && !line.TrimStart().StartsWith("#")) { var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries); for (int i = 1; i < p.Length; i++) l.Add(p[i]); }
                }
            }
            catch { }
            return l;
        }

        static List<string> Strip()
        {
            var keep = new List<string>(); bool inside = false;
            foreach (var line in File.ReadAllLines(File_))
            {
                if (line.Trim() == BEGIN) { inside = true; continue; }
                if (line.Trim() == END) { inside = false; continue; }
                if (!inside) keep.Add(line);
            }
            while (keep.Count > 0 && keep[keep.Count - 1].Trim() == "") keep.RemoveAt(keep.Count - 1);
            return keep;
        }

        public static void Apply(IEnumerable<string> domains) { ApplyBlock(domains.Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("#")).Distinct().ToList(), null, false); }

        // Writes the block between the markers. compact = 9 names per line (smaller file, faster for Windows' DNS Client).
        public static void ApplyBlock(List<string> domains, IEnumerable<string> header, bool compact)
        {
            string bak = Path.Combine(Paths.Backups, "hosts.original");
            if (!File.Exists(bak)) File.Copy(File_, bak);
            var lines = Strip();
            lines.Add(""); lines.Add(BEGIN);
            if (header != null) lines.AddRange(header);
            if (compact)
                for (int i = 0; i < domains.Count; i += 9)
                    lines.Add("0.0.0.0 " + string.Join(" ", domains.GetRange(i, Math.Min(9, domains.Count - i)).ToArray()));
            else
                foreach (var d in domains) lines.Add("0.0.0.0 " + d);
            lines.Add(END);
            WriteHosts(lines);
            Shell.Run("ipconfig.exe", "/flushdns", true);
            Log.W("Hosts block written: {0:N0} domains, {1:N0} lines, {2}.", domains.Count, lines.Count, CleanupModule.Fmt(new FileInfo(File_).Length));
        }

        public static void Remove()
        {
            try { HostsEngine.RemoveTask(); } catch { }
            if (!IsActive()) return;
            WriteHosts(Strip());
            Shell.Run("ipconfig.exe", "/flushdns", true);
            Log.W("Hosts block removed.");
        }

        static void WriteHosts(List<string> lines)
        {
            var attr = File.GetAttributes(File_);
            if ((attr & FileAttributes.ReadOnly) != 0) File.SetAttributes(File_, attr & ~FileAttributes.ReadOnly);
            File.WriteAllLines(File_, lines, new UTF8Encoding(false));
        }
    }
}
