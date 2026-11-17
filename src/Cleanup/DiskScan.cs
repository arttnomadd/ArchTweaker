// ArchTweaker: Disk scan engine: folder tree, treemap layout, cleanup settings and exclusions.
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
    // ================= CLEANUP SETTINGS =================
    public static class CleanExclude
    {
        const string KEY = @"Software\ArchTweaker";
        static List<string> cache;
        public static List<string> All
        {
            get
            {
                if (cache != null) return cache;
                var v = Reg.Get(Hive.HKCU, KEY, "CleanExclude") as string[];
                return cache = v == null ? new List<string>() : v.Where(x => x.Length > 3).ToList();
            }
        }
        public static void Save(IEnumerable<string> paths)
        {
            cache = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(KEY)) k.SetValue("CleanExclude", cache.ToArray(), RegistryValueKind.MultiString);
        }
        public static bool Skip(string path)
        {
            var all = All; if (all.Count == 0) return false;
            foreach (var e in all)
                if (path.Equals(e, StringComparison.OrdinalIgnoreCase) || path.StartsWith(e.TrimEnd(new[] { '\\' }) + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    public static class CleanSettings
    {
        const string KEY = @"Software\ArchTweaker";
        public static readonly MaintTask Task = new MaintTask("cleanup", "Clean temp files", "Weekly: cleans the items ticked by default in Cleanup when they add up to more than the threshold, then shows a notification.", "/SC WEEKLY /D SAT /ST 13:00", "/clean /notify /silent", true);
        public static int MinMB
        {
            get { object o = Reg.Get(Hive.HKCU, KEY, "CleanMinMB"); int v; return o != null && int.TryParse(o.ToString(), out v) ? v : 500; }
            set { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(KEY)) k.SetValue("CleanMinMB", value.ToString()); }
        }
        public static void MarkCleaned(long freed)
        {
            try { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(KEY)) { k.SetValue("LastClean", DateTime.Now.ToString("o")); k.SetValue("LastCleanFreed", freed.ToString()); } } catch { }
        }
        public static DateTime LastClean
        {
            get { object o = Reg.Get(Hive.HKCU, KEY, "LastClean"); DateTime d; return o != null && DateTime.TryParse(o.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out d) ? d : DateTime.MinValue; }
        }

        // /clean /notify from the scheduled task: measure, clean only above the threshold, show a notification.
        public static void Scheduled(bool notify)
        {
            var ts = CleanupModule.Targets().Where(t => t.Default).ToList();
            long total = 0; foreach (var t in ts) { CleanupModule.Measure(t); total += Math.Max(0, t.Size); }
            if (total < (long)MinMB << 20) { Log.W("Scheduled cleanup: {0} found, below the {1} MB threshold: nothing cleaned.", CleanupModule.Fmt(total), MinMB); return; }
            long freed = 0;
            foreach (var t in ts) { int sk; freed += CleanupModule.Clean(t, out sk); }
            MarkCleaned(freed);
            Log.W("Scheduled cleanup freed {0}.", CleanupModule.Fmt(freed));
            if (notify) Balloon(L.T("ArchTweaker cleanup"), L.T("Freed " + CleanupModule.Fmt(freed) + "."));
        }

        public static void Balloon(string title, string text)
        {
            try
            {
                using (var ni = new System.Windows.Forms.NotifyIcon())
                {
                    try { ni.Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath); } catch { ni.Icon = SystemIcons.Information; }
                    ni.Visible = true; ni.ShowBalloonTip(6000, title, text, System.Windows.Forms.ToolTipIcon.Info);
                    var until = DateTime.Now.AddSeconds(7);
                    while (DateTime.Now < until) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(100); }
                    ni.Visible = false;
                }
            }
            catch { }
        }
    }

    // ================= DISK SPACE =================
    public class DirNode
    {
        public string Name, Path; public long Size; public bool IsFiles;   // IsFiles = the loose files of the parent folder
        public List<DirNode> Kids = new List<DirNode>(); public DirNode Parent;
    }

    public static class DiskScan
    {
        // Folder sizes under root. Links and junctions are not followed, so nothing is counted twice.
        public static DirNode Scan(string root, Func<bool> cancelled, Action<long, string> progress)
        {
            var top = new DirNode { Name = root, Path = root };
            long files = 0;
            Walk(top, cancelled, progress, ref files, 0);
            return top;
        }

        static long Walk(DirNode n, Func<bool> cancelled, Action<long, string> progress, ref long files, int depth)
        {
            if (cancelled()) return 0;
            long loose = 0;
            try
            {
                var di = new DirectoryInfo(n.Path);
                foreach (var fi in di.EnumerateFileSystemInfos())
                {
                    if (cancelled()) break;
                    if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((fi.Attributes & FileAttributes.Directory) != 0)
                    {
                        var k = new DirNode { Name = fi.Name, Path = fi.FullName, Parent = n };
                        k.Size = Walk(k, cancelled, progress, ref files, depth + 1);
                        if (k.Size > 0) n.Kids.Add(k);
                    }
                    else
                    {
                        try { loose += ((FileInfo)fi).Length; } catch { }
                        if (++files % 5000 == 0 && progress != null) progress(files, n.Path);
                    }
                }
            }
            catch { }
            if (loose > 0) n.Kids.Add(new DirNode { Name = "[files]", Path = n.Path, Size = loose, IsFiles = true, Parent = n });
            n.Size = n.Kids.Sum(k => k.Size);
            n.Kids = n.Kids.OrderByDescending(k => k.Size).ToList();
            if (depth > 2 && n.Kids.Count > 60) n.Kids = n.Kids.Take(60).ToList();   // deep folders: keep only the biggest
            return n.Size;
        }
    }

    // Squarified treemap layout (Bruls, Huizing, van Wijk).
    public static class Treemap
    {
        public static List<KeyValuePair<DirNode, RectangleF>> Layout(IList<DirNode> items, RectangleF r)
        {
            var res = new List<KeyValuePair<DirNode, RectangleF>>();
            var list = items.Where(i => i.Size > 0).ToList();
            double total = list.Sum(i => (double)i.Size);
            if (total <= 0 || r.Width < 1 || r.Height < 1) return res;
            double scale = r.Width * r.Height / total;
            var areas = list.Select(i => i.Size * scale).ToList();
            int start = 0;
            while (start < list.Count)
            {
                float side = Math.Min(r.Width, r.Height);
                int end = start; double sum = 0, best = double.MaxValue, max = 0, min = double.MaxValue;
                while (end < list.Count)
                {
                    double s2 = sum + areas[end], mx = Math.Max(max, areas[end]), mn = Math.Min(min, areas[end]);
                    double worst = Math.Max(side * side * mx / (s2 * s2), s2 * s2 / (side * side * mn));
                    if (worst > best) break;
                    best = worst; sum = s2; max = mx; min = mn; end++;
                }
                if (sum <= 0 || side <= 0) break;
                bool horiz = r.Width >= r.Height;
                float thick = (float)(sum / side), pos = horiz ? r.Y : r.X;
                for (int i = start; i < end; i++)
                {
                    float len = (float)(areas[i] / thick);
                    res.Add(new KeyValuePair<DirNode, RectangleF>(list[i], horiz ? new RectangleF(r.X, pos, thick, len) : new RectangleF(pos, r.Y, len, thick)));
                    pos += len;
                }
                r = horiz ? new RectangleF(r.X + thick, r.Y, r.Width - thick, r.Height) : new RectangleF(r.X, r.Y + thick, r.Width, r.Height - thick);
                start = end;
            }
            return res;
        }
    }
}
