// 3.42: pages you pin show in a "Pinned" section at the top of the sidebar, under Home.
// Right-click a page in the sidebar (or press the Menu key on it) and choose "Pin to top", or use the
// command palette (Ctrl+K). Stored in HKCU\Software\ArchTweaker\PinnedPages, in the order you pinned them.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArchTweaker
{
    public static class PinnedPages
    {
        public const int Max = 6;
        const string Key = @"Software\ArchTweaker", Value = "PinnedPages";

        public static List<string> Load()
        {
            try
            {
                var a = Reg.Get(Hive.HKCU, Key, Value) as string[];
                return Clean(a ?? new string[0]);
            }
            catch (Exception ex) { Log.W("Pinned pages: {0}", ex.Message); return new List<string>(); }
        }

        public static void Save(IEnumerable<string> pages)
        {
            try
            {
                using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(Key))
                    k.SetValue(Value, Clean(pages).ToArray(), Microsoft.Win32.RegistryValueKind.MultiString);
            }
            catch (Exception ex) { Log.W("Pinned pages: can't save: {0}", ex.Message); }
        }

        // No blanks, no duplicates, Home never (it is always first), at most Max
        public static List<string> Clean(IEnumerable<string> pages)
        {
            return pages.Where(p => !string.IsNullOrWhiteSpace(p) && p != "Home")
                .Distinct(StringComparer.Ordinal).Take(Max).ToList();
        }

        public static bool CanPin(string page) { return !string.IsNullOrEmpty(page) && page != "Home" && page != MainForm.SEARCH; }

        // Pinned → unpinned; unpinned → pinned at the end. When the list is full, the oldest pin makes room.
        public static List<string> Toggle(IEnumerable<string> current, string page)
        {
            var l = Clean(current);
            if (!CanPin(page)) return l;
            if (l.Remove(page)) return l;
            l.Add(page);
            while (l.Count > Max) l.RemoveAt(0);
            return l;
        }
    }
}
