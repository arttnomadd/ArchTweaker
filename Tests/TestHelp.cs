// Helpers shared by the test files.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ArchTweaker;

static class TestHelp
{
    public static string[] Parse(string cmd) { string f, a; Uninstaller.ParseCommand(cmd, out f, out a); return new[] { f, a }; }
    public const string Qh =
"Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)\n" +
"  GUID Alias: SCHEME_BALANCED\n" +
"  Subgroup GUID: 54533251-82be-4824-96c1-47b60b740d00  (Processor power management)\n" +
"    GUID Alias: SUB_PROCESSOR\n" +
"    Power Setting GUID: 893dee8e-2bef-41e0-89c6-b55d0929964c  (Minimum processor state)\n" +
"      GUID Alias: PROCTHROTTLEMIN\n" +
"      Minimum Possible Setting: 0x00000000\n" +
"      Current AC Power Setting Index: 0x00000005\n" +
"      Current DC Power Setting Index: 0x00000005\n" +
"    Power Setting GUID: 0cc5b647-c1df-4637-891a-dec35c318584  (Processor performance core parking min cores for Processor Power Efficiency Class 1)\n" +
"      GUID Alias: CPMINCORES1\n" +
"      Current AC Power Setting Index: 0x00000004\n";
    public static double Lum(System.Drawing.Color c)
    {
        Func<double, double> f = v => { v /= 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); };
        return 0.2126 * f(c.R) + 0.7152 * f(c.G) + 0.0722 * f(c.B);
    }
    public static double Cr(System.Drawing.Color a, System.Drawing.Color b) { double x = Lum(a), y = Lum(b); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }
    public static void Ru(Action a) { string keep = L.Lang; try { L.Use("ru"); a(); } finally { L.Use(keep); } }
    public static List<TweakRow> Rows(params string[] ids) { return ids.Select(i => new TweakRow(Catalog.All.First(t => t.Id == i))).ToList(); }
    public static T2 WithSort<T2>(int mode, Func<T2> f) { int keep = TweakSort.Mode; TweakSort.Mode = mode; try { return f(); } finally { TweakSort.Mode = keep; } }
    public const string Ev = "http://schemas.microsoft.com/win/2004/08/events/event";

    // 3.35.1: the project folder (src/ + lang/ru.tsv), found from the current folder or the test exe's folder upward.
    // null when the tests run without the sources next to them; source-scanning tests then skip.
    public static string RepoRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppDomain.CurrentDomain.BaseDirectory })
        {
            var d = new System.IO.DirectoryInfo(start);
            for (int i = 0; d != null && i < 6; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "src")) && System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "lang", "ru.tsv"))) return d.FullName;
        }
        return null;
    }
    // 3.35.1: on a CI machine the sources must be there; a skip would hide a broken check
    public static bool SkipWithoutSources(string root)
    {
        if (root != null) return false;
        bool ci = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        if (ci) { T.True(false, "the sources (src/, lang/ru.tsv) were not found next to the tests on a CI machine"); return true; }
        Console.WriteLine("  (skipped: sources not found next to the tests)");
        return true;
    }
    public static IEnumerable<string> SourceFiles(string root)
    {
        return System.IO.Directory.GetFiles(System.IO.Path.Combine(root, "src"), "*.cs", System.IO.SearchOption.AllDirectories)
            .Where(f => f.IndexOf("Generated", StringComparison.Ordinal) < 0).OrderBy(f => f, StringComparer.Ordinal);
    }
    // a C# regular string literal body to its value (\" \\ \n \r \t)
    public static string CsUnescape(string s)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
            char n = s[++i];
            sb.Append(n == 'n' ? '\n' : n == 'r' ? '\r' : n == 't' ? '\t' : n);
        }
        return sb.ToString();
    }
}
