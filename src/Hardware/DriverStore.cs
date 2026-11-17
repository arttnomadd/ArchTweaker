// ArchTweaker: Driver store packages (pnputil).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ================= DRIVER STORE =================
    public class DriverPkg
    {
        public string Inf, Original, Provider, Class, Version; public DateTime Date;
        public bool InUse, Newest;
        public bool Removable { get { return !InUse && !Newest; } }
    }

    public static class DriverStore
    {
        static readonly Regex DriverVer = new Regex(@"^\s*DriverVer\s*=\s*([0-9/\-.]+)\s*,\s*([0-9.]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);

        public static List<DriverPkg> Scan()
        {
            var inUse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT InfName FROM Win32_PnPSignedDriver"))
                    foreach (ManagementObject o in q.Get()) { var n = o["InfName"] as string; if (n != null) inUse.Add(n); }
            }
            catch (Exception ex) { Log.W("Driver scan: {0}", ex.Message); return new List<DriverPkg>(); }   // without the in-use list nothing is safe to remove

            var l = new List<DriverPkg>();
            string infDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
            foreach (var f in SafeFiles(infDir, "oem*.inf"))
            {
                string name = Path.GetFileName(f), text;
                try { text = File.ReadAllText(f); } catch { continue; }
                var p = new DriverPkg { Inf = name, InUse = inUse.Contains(name) };
                p.Class = Value(text, "Class"); p.Provider = Value(text, "Provider");
                var m = DriverVer.Match(text);
                if (m.Success)
                {
                    p.Version = m.Groups[2].Value;
                    DateTime dt; if (DateTime.TryParse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt)) p.Date = dt;
                }
                p.Original = OriginalName(name) ?? name;
                l.Add(p);
            }
            MarkNewest(l);
            return l.OrderBy(x => x.Class).ThenBy(x => x.Original).ThenByDescending(x => ParseVer(x.Version)).ToList();
        }

        // In each group of the same driver (original INF name, provider, class) the newest version is kept.
        public static void MarkNewest(List<DriverPkg> l)
        {
            foreach (var g in l.GroupBy(x => (x.Original + "|" + x.Provider + "|" + x.Class).ToLowerInvariant()))
                g.OrderByDescending(x => ParseVer(x.Version)).ThenByDescending(x => x.Date).First().Newest = true;
        }

        static IEnumerable<string> SafeFiles(string dir, string pattern) { try { return Directory.GetFiles(dir, pattern); } catch { return new string[0]; } }

        // DriverDatabase\DriverInfFiles\oemNN.inf → "nvlt.inf_amd64_1234…" → "nvlt.inf"
        static string OriginalName(string oem)
        {
            try
            {
                var v = Reg.Get(Hive.HKLM, @"SYSTEM\DriverDatabase\DriverInfFiles\" + oem, "") as string[];
                string pkg = v != null && v.Length > 0 ? v[v.Length - 1] : null;
                if (pkg == null) return null;
                int i = pkg.IndexOf(".inf_", StringComparison.OrdinalIgnoreCase);
                return i > 0 ? pkg.Substring(0, i + 4) : pkg;
            }
            catch { return null; }
        }

        // "Provider = %NVIDIA%" resolved through [Strings]
        public static string Value(string inf, string key)
        {
            var m = Regex.Match(inf, @"^\s*" + key + @"\s*=\s*(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (!m.Success) return "";
            string v = m.Groups[1].Value.Split(new[] { ';' })[0].Trim().Trim(new[] { '"' });
            if (v.StartsWith("%") && v.EndsWith("%") && v.Length > 2)
            {
                var s = Regex.Match(inf, @"^\s*" + Regex.Escape(v.Trim(new[] { '%' })) + @"\s*=\s*""?(.+?)""?\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                if (s.Success) v = s.Groups[1].Value;
            }
            return v;
        }

        public static Version ParseVer(string s) { Version v; return Version.TryParse(s ?? "", out v) ? v : new Version(0, 0); }

        // Exports each package to the backup folder first, then deletes it. pnputil refuses packages a
        // device still uses (no /force is ever passed), so this can't remove a driver that's in use.
        public static int Remove(IEnumerable<DriverPkg> pkgs, out string backup)
        {
            backup = Path.Combine(Paths.Backups, "drivers-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(backup);
            string infDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
            int n = 0;
            foreach (var p in pkgs.Where(x => x.Removable))
            {
                string dir = Path.Combine(backup, Path.GetFileNameWithoutExtension(p.Inf));
                Directory.CreateDirectory(dir);
                Shell.Run("pnputil.exe", "/export-driver " + p.Inf + " \"" + dir + "\"", true);
                if (Directory.GetFiles(dir).Length == 0) { Log.W("Driver {0}: export failed, not removed", p.Inf); continue; }
                Shell.Run("pnputil.exe", "/delete-driver " + p.Inf, true);
                if (!File.Exists(Path.Combine(infDir, p.Inf))) { n++; Log.W("Removed old driver {0} ({1} {2}); backup in {3}", p.Inf, p.Original, p.Version, dir); }
                else Log.W("Driver {0} was not removed (Windows still needs it)", p.Inf);
            }
            return n;
        }
    }
}
