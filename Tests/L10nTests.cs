// Tests for src/App/L10n.cs and lang/ru.tsv.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ArchTweaker;

static class L10nTests
{
    [Test] public static void RussianPlurals()
    {
        T.Eq(L.Plural("1", new[] { "a", "b", "c" }), "a", "1");
        T.Eq(L.Plural("3", new[] { "a", "b", "c" }), "b", "3");
        T.Eq(L.Plural("11", new[] { "a", "b", "c" }), "c", "11");
        T.Eq(L.Plural("22", new[] { "a", "b", "c" }), "b", "22");
        T.Eq(L.Plural("25", new[] { "a", "b", "c" }), "c", "25");
    }

    [Test] public static void Translates_Exact_Pattern_Fragment_Lines() { TestHelp.Ru(delegate
    {
        T.Eq(L.T("Home"), "Главная", "exact");
        T.Eq(L.T("Apply changes"), "Применить изменения", "button");
        T.Eq(L.T("5 changes applied"), "5 изменений применено", "plural pattern");
        T.Eq(L.T("2 changes applied · 1 needs a restart"), "2 изменения применены · 1 требует перезагрузки", "nested patterns");
        T.Eq(L.T("Freed 94.4 MB."), "Освобождено 94.4 МБ.", "pattern keeps the number, unit translated");
        T.Eq(L.T("• Enable Game Mode\n• Disable hibernation"), "• Включить игровой режим\n• Отключить гибернацию", "list lines");
        T.Eq(L.T("Background recording: On (Game DVR)"), "Фоновая запись: Вкл. (Game DVR)", "label: value");
        T.Eq(L.T("C:\\Windows\\Temp"), "C:\\Windows\\Temp", "data stays");
    }); }

    [Test] public static void English_IsUntouched()
    {
        string keep = L.Lang; L.Use("en");
        try { T.Eq(L.T("Home"), "Home", "en"); T.True(!L.Active, "inactive"); }
        finally { L.Use(keep); }
    }

    [Test] public static void EveryCategoryAndPageHasRussian() { TestHelp.Ru(delegate
    {
        foreach (var c in Catalog.Categories) T.True(L.T(c) != c, "category " + c);
        foreach (var t in Catalog.All) T.True(L.T(t.Title) != t.Title, "title " + t.Id);
    }); }

    [Test] public static void NewInterfaceText_HasRussian()
    {
        string keep = L.Lang; L.Use("ru");
        try
        {
            foreach (var s in TweakSort.Names.Concat(LogStore.FilterNames).Concat(new[] { "Slim sidebar", "Follow Windows theme", "Text size", "Undo", "PAGE SUMMARY", "Skip setup", "Next", "Back", "Finish", "Welcome to ArchTweaker", "Select the recommended tweaks", "High gain only" }))
                T.True(L.T(s) != s, "ru: " + s);
            T.Eq(L.T("Step 2 of 3"), "Шаг 2 из 3", "step pattern");
            T.Eq(L.T("and 4 more"), "и ещё 4", "and-more pattern");
        }
        finally { L.Use(keep); }
    }

    [Test] public static void NewTweaks_ArePresent_WithGainAndRussian()
    {
        string[] ids = { "il_timers", "gd_audio_power", "mmcss_latency", "gamebar_links", "ui_snap", "ui_startrec", "ui_copilotbtn", "ui_dynlight", "ui_langhotkey",
            "priv_cloudsearch", "priv_paintai", "priv_lockscreen", "priv_locktoasts", "priv_devmeta", "priv_ink", "sys_archiving", "sys_storeauto", "sys_featurelock",
            "pp_deepsleep", "net_ports", "cmd_rsc", "cmd_timerclean", "svc_killer", "dz_sac", "dz_bitlocker", "dz_audiooem" };
        TestHelp.Ru(delegate
        {
            foreach (var id in ids)
            {
                var t = Catalog.All.FirstOrDefault(x => x.Id == id);
                T.True(t != null, id + " exists"); if (t == null) continue;
                T.True(t.Gain != "No measurable speed effect expected.", id + " has its own gain text");
                T.True(L.T(t.Desc) != t.Desc, id + " description in Russian");
                T.True(L.T(t.Gain) != t.Gain, id + " gain in Russian");
                if (t.Breaks != null) T.True(L.T(t.Breaks) != t.Breaks, id + " breaks text in Russian");
            }
            foreach (var t in Catalog.All.Where(x => x.Dangerous)) T.True(L.T(t.Breaks) != t.Breaks, t.Id + " breaks text in Russian");
            T.Eq(L.T("I understand what these 3 break"), "Я понимаю, что сломают эти 3", "ack pattern");
            T.True(L.T("5 changes are from the Danger zone. Read what each one breaks:").StartsWith("5 изменений"), "plural head");
        });
    }

    [Test] public static void Effects_AreTranslated()
    {
        string keep = L.Lang;
        try
        {
            L.Use("ru");
            foreach (var t in Catalog.All)
            {
                if (!string.IsNullOrEmpty(t.Off)) T.True(L.T(t.Off) != t.Off, t.Id + " 'turns off' in Russian");
                if (!string.IsNullOrEmpty(t.Breaks)) T.True(L.T(t.Breaks) != t.Breaks, t.Id + " 'can break' in Russian");
            }
        }
        finally { L.Use(keep); }
    }

    [Test] public static void Changelog_FullHistory()
    {
        var v = WhatsNew.Versions(Changelog.All);
        T.True(v.Count >= 60, "every version is in the history");
        T.True(v[0].StartsWith(Program.Version + ":"), "newest first");
        T.Eq(v[v.Count - 1], "1.2", "oldest last, old heading normalised");
        T.True(v.Contains("1.7: online hosts blocklists"), "What's new in 1.7 written as a version heading");
        T.True(v.Contains("3.6.3"), "heading without a title kept");
        T.True(v.All(x => char.IsDigit(x[0])), "only version sections, no README chapters");
    }

    // 3.35.1: every L.T("...") / L.F("...") text in the sources has a Russian line (3.35.0 had 39 without one)
    [Test] public static void EveryUiText_HasARussianLine()
    {
        string root = TestHelp.RepoRoot();
        if (TestHelp.SkipWithoutSources(root)) return;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in L10nRu.Table.Split('\n'))
        {
            string line = raw.TrimEnd('\r'); int tab = line.IndexOf('\t');
            if (line.Length == 0 || line[0] == '#' || tab <= 0) continue;
            keys.Add((string)T.Call(typeof(L), "Unescape", line.Substring(0, tab)));
        }
        var lit = new Regex(@"\bL\.(?:T|F)\(\s*""((?:[^""\\]|\\.)*)""");
        var missing = new List<string>();
        foreach (var f in TestHelp.SourceFiles(root))
            foreach (Match m in lit.Matches(string.Join("\n", File.ReadAllLines(f).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)).ToArray())))
            {
                string en = TestHelp.CsUnescape(m.Groups[1].Value);
                if (en.Trim().Length > 0 && !keys.Contains(en)) missing.Add(Path.GetFileName(f) + ": " + en);
            }
        T.Eq(missing.Count, 0, "texts without a Russian line (add them to lang/ru.tsv, then run tools/gen_l10n.py): " + string.Join(" | ", missing.Distinct().Take(10).ToArray()));
    }
}
