// ArchTweaker: Tweak sorting and the "recommended for this PC" set.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace ArchTweaker
{
    // ================= SORT =================
    // How rows are ordered on a tweak page. Saved per user; catalog order is the default.
    public static class TweakSort
    {
        public static readonly string[] Names = { "Catalog order", "Highest gain first", "Not applied first", "A–Z" };
        static int mode = -1;
        public static int Mode
        {
            get { if (mode < 0) { object o = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "Sort"); mode = o is int && (int)o >= 0 && (int)o < Names.Length ? (int)o : 0; } return mode; }
            set { mode = value; try { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(@"Software\ArchTweaker")) k.SetValue("Sort", value, Microsoft.Win32.RegistryValueKind.DWord); } catch { } }
        }
        // rows in display order; "applied" is what each row's switch shows now
        public static List<TweakRow> Order(List<TweakRow> rows)
        {
            var idx = rows.Select((r, i) => new { r, i }).ToList();
            switch (Mode)
            {
                case 1: return idx.OrderByDescending(x => x.r.T.Applicable).ThenByDescending(x => x.r.T.Impact).ThenBy(x => x.i).Select(x => x.r).ToList();
                case 2: return idx.OrderByDescending(x => x.r.T.Applicable).ThenBy(x => x.r.Checked).ThenByDescending(x => x.r.T.Impact).ThenBy(x => x.i).Select(x => x.r).ToList();
                case 3: return idx.OrderBy(x => L.T(x.r.T.Title), StringComparer.CurrentCultureIgnoreCase).Select(x => x.r).ToList();
                default: return rows;
            }
        }
    }

    // ================= RECOMMENDED =================
    // The PC advisor's picks for this hardware, worked out once in the background and shown as a badge on each row.
    public static class Recommend
    {
        static volatile HashSet<string> ids;
        static int started;
        static readonly List<Action> waiters = new List<Action>();   // every window that asked before the result was in
        public static bool Ready { get { return ids != null; } }
        public static bool Has(Tweak t) { var s = ids; return s != null && s.Contains(t.Id); }
        public static IEnumerable<Tweak> In(string category) { return Catalog.All.Where(t => t.Category == category && t.Applicable && Mode.Allows(t) && Has(t)); }
        public static void Warm(Action done)
        {
            lock (waiters)
            {
                if (ids == null) { if (done != null) waiters.Add(done); }
                else { if (done != null) done(); return; }
            }
            if (Interlocked.Exchange(ref started, 1) == 1) return;   // already running: done() is called with the others
            ThreadPool.QueueUserWorkItem(delegate
            {
                HashSet<string> r;
                try { r = new HashSet<string>(Advisor.RecommendedIds(), StringComparer.OrdinalIgnoreCase); }
                catch (Exception ex) { Log.W("Recommended picks: {0}", ex.Message); r = new HashSet<string>(); }
                List<Action> call;
                lock (waiters) { ids = r; call = waiters.ToList(); waiters.Clear(); }
                foreach (var a in call) try { a(); } catch { }
            });
        }
    }
}
