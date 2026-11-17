// ArchTweaker: Power plan operations used by tweaks: create, activate and set values.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static partial class PowerPlans
    {

        public static void CreateMax()
        {
            string g = Catalog.MAXPLAN;
            if (Shell.Run("powercfg.exe", "/list", true).IndexOf(g, StringComparison.OrdinalIgnoreCase) < 0)
            {
                string o = Shell.Run("powercfg.exe", "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 " + g, true);
                if (o.IndexOf(g, StringComparison.OrdinalIgnoreCase) < 0)
                    Shell.Run("powercfg.exe", "-duplicatescheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c " + g, true);
                Shell.Run("powercfg.exe", "-changename " + g + " \"ArchTweaker Max Performance\" \"Maximum performance and lowest latency. Created by ArchTweaker.\"", true);
            }
            Shell.Run("powercfg.exe", "/setactive " + g, true);
            PowerCache.Invalidate();
            Log.W("  Active power plan: ArchTweaker Max Performance");
        }

        public static void RemoveMax()
        {
            Shell.Run("powercfg.exe", "/setactive 381b4222-f694-41f0-9685-ff5bb260df2e", true);
            Shell.Run("powercfg.exe", "/delete " + Catalog.MAXPLAN, true);
            PowerCache.Invalidate();
        }

        public static void RemoveDuplicates()
        {
            dupCache = null;
            foreach (var d in Duplicates())
            {
                Shell.Run("powercfg.exe", "/delete " + d.Key, true);
                Log.W("Deleted duplicate power plan: {0} ({1})", d.Value, d.Key);
            }
            dupCache = null;
            PowerCache.Invalidate();
        }

        // 3.6.2: plans whose name repeats; built-in and active plans are never listed, and one plan of each name is kept
        // cached 15 s: the state check runs on every page refresh and would start two powercfg processes each time
        static DateTime dupAt; static List<KeyValuePair<string, string>> dupCache;
        public static List<KeyValuePair<string, string>> Duplicates()
        {
            if (dupCache != null && (DateTime.Now - dupAt).TotalSeconds < 15) return dupCache;
            dupCache = Duplicates(Shell.Run("powercfg.exe", "/list", true), PowerCache.ActiveScheme());
            dupAt = DateTime.Now;
            return dupCache;
        }
        public static List<KeyValuePair<string, string>> Duplicates(string list, string active)
        {
            var keep = new[] { "381b4222-f694-41f0-9685-ff5bb260df2e", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "a1841308-3541-4fab-bc81-f71556f20b4a", "e9a42b02-d5df-448d-aa00-03f14749eb61" };
            var rx = new System.Text.RegularExpressions.Regex("([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\\s+\\((.*?)\\)");
            var plans = (list ?? "").Split('\n').Select(l => rx.Match(l)).Where(m => m.Success)
                .Select(m => new { G = m.Groups[1].Value, N = m.Groups[2].Value.Trim() }).ToList();
            Func<string, bool> protectedPlan = g => keep.Contains(g.ToLowerInvariant()) || (active ?? "").IndexOf(g, StringComparison.OrdinalIgnoreCase) >= 0;
            var result = new List<KeyValuePair<string, string>>();
            foreach (var grp in plans.GroupBy(p => p.N, StringComparer.OrdinalIgnoreCase))
            {
                if (grp.Count() < 2) continue;
                // keep a protected plan if the group has one, otherwise the first one listed
                var keeper = grp.FirstOrDefault(p => protectedPlan(p.G)) ?? grp.First();
                foreach (var p in grp) if (p != keeper && !protectedPlan(p.G)) result.Add(new KeyValuePair<string, string>(p.G, p.N));
            }
            return result;
        }

    }
}
