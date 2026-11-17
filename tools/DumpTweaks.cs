// Writes docs/TWEAKS.md: every tweak in the catalog with its mode, risk, impact and expected gain.
// Build and run (from the repo root, Mono):
//   mcs -langversion:5 -main:DumpTweaks -out:/tmp/dump.exe tools/DumpTweaks.cs -recurse:'src/*.cs' <refs> && mono /tmp/dump.exe docs/TWEAKS.md
using System;
using System.IO;
using System.Linq;
using System.Text;
using ArchTweaker;

static class DumpTweaks
{
    static string Cell(string s) { return (s ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " "); }

    static int Main(string[] a)
    {
        string outFile = a.Length > 0 ? a[0] : "docs/TWEAKS.md";
        var all = Catalog.All;
        var sb = new StringBuilder();
        sb.Append("# ArchTweaker tweak list\n\n");
        sb.Append("Generated from the catalog of ArchTweaker " + Program.Version + " by `tools/DumpTweaks.cs`. Do not edit by hand.\n\n");
        sb.Append(all.Count + " tweaks. **Lite** = shown in Lite mode (safe, not experimental). **Pro** = shown only in Pro mode.\n");
        sb.Append("Impact: **HIGH** = 5 %+ / GBs / seconds · **MEDIUM** = 1–5 % or clearly measurable latency · **LOW** = under 1 %, consistency or ms-level feel · **NONE** = privacy, appearance or convenience.\n");
        sb.Append("The ranges are typical public-test results, not guarantees. The details pane in the app shows the same text, plus what each tweak turns off and can break.\n");
        foreach (var g in all.GroupBy(t => t.Category).OrderBy(g => Array.IndexOf(Catalog.Categories, g.Key)))   // sidebar order
        {
            sb.Append("\n## " + g.Key + " (" + g.Count() + ")\n\n| Tweak | Mode | Risk | Impact | Expected gain |\n|---|---|---|---|---|\n");
            foreach (var t in g)
                sb.Append("| " + Cell(t.Title) + (t.Experimental ? " *(experimental)*" : "") + " | " + (Mode.LiteTweak(t) ? "Lite" : "Pro") + " | " + t.Risk + " | " + t.ImpactName + " | " + Cell(t.Gain) + " |\n");
        }
        File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine("{0}: {1} tweaks", outFile, all.Count);
        // 3.35: keep the tweak count in README.md in step with the catalog (3.34 said 276, the catalog had 280)
        string readme = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outFile)), "..", "README.md");
        if (File.Exists(readme))
        {
            string r = File.ReadAllText(readme);
            string r2 = System.Text.RegularExpressions.Regex.Replace(r, @"\(\d+ tweaks\)", "(" + all.Count + " tweaks)");
            if (r2 != r) { File.WriteAllText(readme, r2, new UTF8Encoding(false)); Console.WriteLine("README.md: tweak count set to {0}", all.Count); }
        }
        return 0;
    }
}
