// Tests for src/Ui.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ArchTweaker;

static class UiTests
{
    [Test] public static void Scale_HelpersRoundTo96DpiUnits()
    {
        int old = Theme.Dpi;
        try
        {
            Theme.Dpi = 144;
            T.True(Theme.S(10) == 15 && Theme.S(44, 26) == new System.Drawing.Size(66, 39), "150 % scaling");
            Theme.Dpi = 96;
            T.True(Theme.S(37) == 37, "100 % is identity");
        }
        finally { Theme.Dpi = old; }
    }

    [Test] public static void RowWrap_BreaksOnWords_WithinWidth()
    {
        var f = Theme.FS;
        List<string> lines;
        try { lines = (List<string>)T.Call(typeof(TweakRow), "Wrap", "one two three four five six seven eight nine ten", f, 120); }
        catch (System.Reflection.TargetInvocationException ex) { Console.WriteLine("  (inner: " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message + ")"); throw; }
        T.True(lines.Count >= 2, "wraps to several lines");
        T.True(string.Join(" ", lines.ToArray()) == "one two three four five six seven eight nine ten", "no words lost");
    }

    // 3.40: back / forward page history
    [Test] public static void NavHistory_BackAndForward()
    {
        var h = new NavHistory(p => p != "Search");
        T.False(h.CanBack || h.CanForward, "empty at start");
        h.Visit(null, "Home"); h.Visit("Home", "Privacy"); h.Visit("Privacy", "Latency");
        T.Eq(h.Back("Latency"), "Privacy", "back one");
        T.Eq(h.Back("Privacy"), "Home", "back two");
        T.Eq(h.Back("Home"), null, "nothing before Home");
        T.Eq(h.Forward("Home"), "Privacy", "forward again");
        h.Visit("Privacy", "Cleanup");
        T.False(h.CanForward, "a new page clears forward");
        T.Eq(h.Back("Cleanup"), "Privacy", "back to where the new page came from");
        h.Visit("Privacy", "Privacy");
        T.Eq(h.Back("Privacy"), "Home", "the same page twice is not a step");
        var s = new NavHistory(p => p != "Search");
        s.Visit("Home", "Search"); s.Visit("Search", "Gaming");
        T.Eq(s.Back("Gaming"), "Home", "search results are not a step back");
        var m = new NavHistory(null);
        for (int i = 0; i < NavHistory.Max + 10; i++) m.Visit("p" + i, "p" + (i + 1));
        int n = 0; while (m.Back("x") != null) n++;
        T.Eq(n, NavHistory.Max, "history keeps the last " + NavHistory.Max + " pages");
        var r = new NavHistory(null);
        r.Visit("Home", "A"); r.Visit("A", "B"); r.Visit("B", "A"); r.Visit("A", "C");
        var rec = r.Recent("C", 5);
        T.Eq(string.Join(",", rec.ToArray()), "A,B,Home", "recent pages: newest first, each once, not the current one");
        T.Eq(r.Recent("C", 1).Count, 1, "at most max");
    }

    // 3.39: what Ctrl+K picks for a query (the keyboard part is an e2e test on real desktops)
    [Test] public static void Palette_RanksExactPageFirst()
    {
        Func<string, string, string, CommandPalette.Cmd> c = (kind, text, search) => new CommandPalette.Cmd { Kind = kind, Text = text, Search = search ?? text };
        var all = new List<CommandPalette.Cmd>
        {
            c("Page", "Tools & backup", null), c("Tool", "Free disk space", null), c("Tweak", "Disk space warning off", "Disk space warning off System"),
            c("Action", "Disk space report", null), c("Page", "Disk space", null), c("Page", "Cleanup", null),
        };
        var r = CommandPalette.Rank(all, "Disk space");
        T.Eq(r[0].Text, "Disk space", "the exact page name wins");
        T.True(r.All(x => x.Search.IndexOf("disk", StringComparison.OrdinalIgnoreCase) >= 0 && x.Search.IndexOf("space", StringComparison.OrdinalIgnoreCase) >= 0), "every word must match");
        T.False(r.Any(x => x.Text == "Tools & backup" || x.Text == "Cleanup"), "pages without the words are left out");
        T.Eq(CommandPalette.Rank(all, "  disk SPACE ")[0].Text, "Disk space", "case and spaces don't matter");
        T.Eq(CommandPalette.Rank(all, "disk")[0].Kind, "Page", "starts-with ties: a page before an action or tweak");
        T.Eq(CommandPalette.Rank(all, "").Count, all.Count, "empty query lists everything");
        T.Eq(CommandPalette.Rank(all, "")[0].Kind, "Page", "pages first in the full list");
        T.Eq(CommandPalette.Rank(all, null).Count, all.Count, "null query is empty");
    }

    [Test] public static void EveryAccent_PassesContrast_InBothThemes()
    {
        int keepA = Theme.AccentIndex, keepS = Theme.Scheme; var keepC = Theme.SchemeCustom;
        try
        {
            Theme.SchemeCustom = Theme.DefaultSchemeCustom;   // 3.38.2: not the colour saved on this PC
            for (int i = 0; i < Theme.AccentNames.Length; i++)
                for (int sc = 0; sc < Theme.SchemeNames.Length; sc++)
                {
                    Theme.AccentIndex = i; Theme.SetScheme(sc);
                    string n = Theme.AccentNames[i] + "/" + Theme.SchemeNames[sc];
                    T.True(TestHelp.Cr(Theme.Text, Theme.Bg) >= 7 && TestHelp.Cr(Theme.Text, Theme.Panel) >= 7, n + " text on surfaces");
                    T.True(TestHelp.Cr(Theme.Muted, Theme.Bg) >= 4.5 && TestHelp.Cr(Theme.Muted, Theme.Panel) >= 4.5 && TestHelp.Cr(Theme.Muted, Theme.PanelHover) >= 4.5, n + " muted on surfaces");
                    T.True(TestHelp.Cr(Theme.Warn, Theme.Panel) >= 4.5 && TestHelp.Cr(Theme.Bad, Theme.Panel) >= 4.5 && TestHelp.Cr(Theme.Ok, Theme.Panel) >= 4.5, n + " status colours");
                    T.True(TestHelp.Cr(Theme.OnAccent, Theme.Accent) >= 4.5, n + " text on accent");
                    T.True(TestHelp.Cr(Theme.OnAccent, Theme.AccentDark) >= 4.5, n + " text on hover");
                    T.True(TestHelp.Cr(Theme.Accent, Theme.Bg) >= 3 && TestHelp.Cr(Theme.Accent, Theme.Panel) >= 3 && TestHelp.Cr(Theme.Accent, Theme.Side) >= 3, n + " accent on surfaces");
                    T.True(TestHelp.Cr(Theme.Text, Theme.PendingBg) >= 4.5 && TestHelp.Cr(Theme.Text, Theme.Sel) >= 4.5, n + " text on tinted rows");
                }
        }
        finally { Theme.SchemeCustom = keepC; Theme.AccentIndex = keepA; Theme.SetScheme(keepS); }
    }

    [Test] public static void HeaderSwitch_ReturnsToLastDarkScheme()
    {
        int keep = Theme.Scheme;
        try
        {
            Theme.SetScheme(1); Theme.SetScheme(2); Theme.Set(false);   // arrange: last light = 1 (other tests cycle every scheme)
            T.True(Theme.Scheme == 1 && !Theme.Dark, "switch to light");
            Theme.Set(true);
            T.True(Theme.Scheme == 2 && Theme.Dark, "back to OLED black");
            Theme.SetScheme(2); T.True(Theme.Dark, "OLED is dark");
            Theme.SetScheme(99); T.True(Theme.Scheme == 0, "out of range falls back to Carbon dark");
        }
        finally { Theme.SetScheme(keep); }
    }

    [Test] public static void Sort_CatalogOrder_KeepsInputOrder()
    {
        var all = Catalog.All.Where(t => t.Applicable).Take(5).Select(t => t.Id).ToArray();
        var rows = TestHelp.Rows(all);
        var got = TestHelp.WithSort(0, () => TweakSort.Order(rows)).Select(r => r.T.Id).ToArray();
        T.Eq(string.Join(",", got), string.Join(",", all), "catalog order unchanged");
    }

    [Test] public static void Sort_HighestGainFirst_IsNonIncreasingImpact()
    {
        var rows = TestHelp.Rows(Catalog.All.Where(t => t.Applicable).Take(12).Select(t => t.Id).ToArray());
        var got = TestHelp.WithSort(1, () => TweakSort.Order(rows)).Select(r => r.T.Impact).ToList();
        bool ok = true; for (int i = 1; i < got.Count; i++) if (got[i] > got[i - 1]) ok = false;
        T.True(ok, "impact never rises down the list");
    }

    [Test] public static void Sort_AtoZ_IsAlphabetical()
    {
        var rows = TestHelp.Rows(Catalog.All.Take(10).Select(t => t.Id).ToArray());
        var got = TestHelp.WithSort(3, () => TweakSort.Order(rows)).Select(r => L.T(r.T.Title)).ToList();
        T.True(got.SequenceEqual(got.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)), "A–Z");
    }

    [Test] public static void TextSize_GrowsBodyFont_LogoKeepsSize()
    {
        int keep = Theme.TextSize;
        try
        {
            Theme.SetTextSize(0); float f0 = Theme.F.Size, l0 = Theme.LogoFont.Size;
            Theme.SetTextSize(2); float f2 = Theme.F.Size, l2 = Theme.LogoFont.Size;
            T.True(Math.Abs(f2 / f0 - 1.3f) < 0.02f, "body text 130 %");
            T.True(Math.Abs(l2 - l0) < 0.05f, "logo unchanged");
        }
        finally { Theme.SetTextSize(keep); }
    }

    [Test] public static void HighGainFilter_KeepsOnlyHighImpact()
    {
        int keep = TweakFilter.Mode;
        try
        {
            TweakFilter.Mode = 4;
            T.True(Catalog.All.All(t => TweakFilter.Pass(t, false, "") == (t.Impact >= 3)), "only Impact >= 3 passes");
        }
        finally { TweakFilter.Mode = keep; }
    }

    [Test] public static void Toggle_DrawsEveryKnobPosition()
    {
        using (var bmp = new System.Drawing.Bitmap(60, 40)) using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            foreach (var p in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                Toggle.Draw(g, new System.Drawing.RectangleF(0, 0, 44, 26), p, false, true, Theme.Panel, false);
            // the knob reaches the far right only when on
            T.True(bmp.GetPixel(30, 13).ToArgb() != bmp.GetPixel(59, 39).ToArgb(), "something drawn");
        }
    }

    [Test] public static void Slide_SnapsWhenControlIsNotShown()
    {
        var c = new System.Windows.Forms.Panel(); float v = 0f;
        Motion.Slide(c, () => v, x => v = x, true, c.ClientRectangle);
        T.Eq(v, 1f, "hidden control: no animation, final position at once");
        T.False(Motion.Running(c), "nothing left running");
    }

    [Test] public static void DefaultCustomAccent_IsNotViolet()
    {
        float h = Theme.DefaultCustom.GetHue();
        T.True(h < 250f || h > 300f, "hue " + h + " outside violet/lavender");
    }

    [Test] public static void UacDim_LeftTheDangerZone()
    {
        var t = Catalog.All.First(x => x.Id == "uacdim");
        T.Eq(t.Category, "System", "uacdim is an ordinary System tweak");
        T.Eq(t.Risk, Risk.Moderate, "moderate");
    }

    [Test] public static void DangerBanner_LaysOutAndFollowsTheLock()
    {
        using (var b = new DangerBanner { Width = 700 })
        {
            int h = b.Height;
            T.True(h > 80, "banner has height");
            var btn = b.Controls.OfType<System.Windows.Forms.Button>().First();
            string locked = btn.Text;
            DangerZone.Unlocked = true;
            try { T.True(btn.Text != locked, "button text follows the lock"); }
            finally { DangerZone.Unlocked = false; }
            T.Eq(btn.Text, locked, "back to locked text");
        }
    }

    [Test] public static void V3102_CustomScheme_ReadableForAnyColour()
    {
        int scheme = Theme.Scheme, acc = Theme.AccentIndex; var keep = Theme.SchemeCustom;
        Func<System.Drawing.Color, System.Drawing.Color, double> cr = AccentMaker.Contrast;
        try
        {
            string[] picks = { "#000000", "#FFFFFF", "#777777", "#808080", "#FF0000", "#00FF00", "#0000FF", "#FFC0CB", "#1A1B2E", "#F5DEB3", "#4B0082", "#008080", "#008040" };   // 3.38.2: #008040 gave status text under 4.5:1
            foreach (var hex in picks)
                for (int a = 0; a < Theme.AccentNames.Length; a++)
                {
                    Theme.SchemeCustom = System.Drawing.ColorTranslator.FromHtml(hex); Theme.AccentIndex = a; Theme.SetScheme(Theme.CustomScheme);
                    string n = hex + "/" + Theme.AccentNames[a] + ": ";
                    T.True(cr(Theme.Text, Theme.Bg) >= 7 && cr(Theme.Text, Theme.Panel) >= 7 && cr(Theme.Text, Theme.PanelHover) >= 7, n + "text 7:1");
                    T.True(cr(Theme.Muted, Theme.Bg) >= 4.5 && cr(Theme.Muted, Theme.Panel) >= 4.5 && cr(Theme.Muted, Theme.Side) >= 4.5 && cr(Theme.Muted, Theme.PendingBg) >= 4.5, n + "muted 4.5:1");
                    T.True(cr(Theme.OnAccent, Theme.Accent) >= 4.5 && cr(Theme.OnAccent, Theme.AccentDark) >= 4.5, n + "button text 4.5:1");
                    T.True(cr(Theme.Accent, Theme.Bg) >= 3 && cr(Theme.Accent, Theme.Panel) >= 3, n + "accent 3:1");
                    T.True(cr(Theme.Ok, Theme.Panel) >= 4.5 && cr(Theme.Warn, Theme.Panel) >= 4.5 && cr(Theme.Bad, Theme.Panel) >= 4.5 && cr(Theme.Ok, Theme.PanelHover) >= 4.5, n + "status 4.5:1");
                    T.True(cr(Theme.Text, Theme.Sel) >= 4.5 && cr(Theme.Text, Theme.PendingBg) >= 4.5 && cr(Theme.LogFg, Theme.LogBg) >= 4.5, n + "tints and log 4.5:1");
                    T.True(Theme.Dark == SchemeMaker.IsDark(Theme.SchemeCustom), n + "dark follows the colour");
                }
        }
        finally { Theme.SchemeCustom = keep; Theme.AccentIndex = acc; Theme.SetScheme(scheme); }
    }

    [Test] public static void V310_SchemesAndProgressColours()
    {
        int scheme = Theme.Scheme, acc = Theme.AccentIndex, prog = Theme.ProgressIndex;
        try
        {
            T.Eq(Theme.SchemeNames.Length, 4, "four schemes (3.37: Carbon dark, Carbon light, OLED black, Custom)"); T.Eq(Theme.CustomScheme, Theme.SchemeNames.Length - 1, "Custom is last");
            T.True(Theme.IsDarkScheme(0) && !Theme.IsDarkScheme(1) && Theme.IsDarkScheme(2), "dark, light, OLED dark");
            T.Eq(Theme.AccentNames[Theme.PinkAccent], "Pink", "pink accent");
            T.Eq(Theme.AccentNames[Theme.AccentNames.Length - 1], "Custom", "Custom stays last");
            Theme.AccentIndex = Theme.PinkAccent; Theme.SetScheme(0);
            T.Eq(Theme.AccentIndex, Theme.PinkAccent, "a scheme leaves the accent alone");
            Theme.ProgressIndex = 0; Theme.SetScheme(0); T.Eq(Theme.Progress.ToArgb(), Theme.On.ToArgb(), "default progress = status green");
            Theme.ProgressIndex = 1; Theme.SetScheme(0); T.Eq(Theme.Progress.ToArgb(), Theme.Accent.ToArgb(), "Accent follows the accent");
            var saved = Theme.ProgressCustom; Theme.ProgressCustom = System.Drawing.Color.FromArgb(0x1A, 0x1A, 0x1C);
            var fixedC = Theme.ProgressColour(Theme.ProgressNames.Length - 1, true, Theme.On, Theme.Accent, System.Drawing.Color.FromArgb(0x16, 0x16, 0x18), System.Drawing.Color.FromArgb(0xEC, 0xEC, 0xEE));
            T.True(AccentMaker.Contrast(fixedC, System.Drawing.Color.FromArgb(0x16, 0x16, 0x18)) >= 3.0, "near-black custom colour is lifted to 3:1");
            Theme.ProgressCustom = saved;
        }
        finally { Theme.AccentIndex = acc; Theme.ProgressIndex = prog; Theme.SetScheme(scheme); }
    }

    // 3.14: SysInfo text becomes one card per block; a line without a name continues the block
    [Test] public static void KvCard_Parse()
    {
        string text = "OS".PadRight(22) + "Windows 11\r\n" + "Computer".PadRight(22) + "PC  (user: a)\r\n\r\n" + "CPU".PadRight(22) + "i9\r\n" + "RAM".PadRight(22) + "32 GB\r\n" + "".PadRight(22) + "DIMM1 16 GB\r\n\r\n\r\n" + "GPU".PadRight(22) + "RTX\r\n";
        var cards = KvCard.Parse(text);
        T.Eq(cards.Count, 3, "three blocks, empty ones skipped");
        T.True(cards[0].CopyText.Contains("Windows 11") && cards[0].CopyText.Contains("Computer"), "first card has both rows");
        T.True(cards[1].CopyText.Contains("DIMM1 16 GB"), "the continuation line stays in its card");
        T.Eq(KvCard.Parse("").Count, 0, "no text, no cards");
        var lng = KvCard.Parse("Volume D:\\a-very-long-mount-point  12 GB free");
        T.True(lng[0].CopyText.Contains("mount-point") && lng[0].CopyText.TrimEnd().EndsWith("12 GB free"), "a name longer than the column keeps its whole value");
    }

    // 3.16: the empty state's first sentence is its title; short texts ending in "…" show placeholder rows
    [Test] public static void EmptyState_Text()
    {
        string t, b;
        EmptyState.Split("No removable Store apps found.", out t, out b);
        T.Eq(t, "No removable Store apps found.", "one sentence: title only"); T.Eq(b, "", "no body");
        EmptyState.Split("Press \"Find slow drivers\" to record driver activity. Double-click a driver to see what it is.", out t, out b);
        T.True(t.EndsWith("activity.") && b.StartsWith("Double-click"), "first sentence becomes the title, the rest the body");
        EmptyState.Split("Version 3.16.0 is here", out t, out b);
        T.Eq(b, "", "a dot inside a number does not split");
        T.True(EmptyState.IsLoadingText("Loading…") && EmptyState.IsLoadingText("Reading counters…"), "loading texts");
        T.True(!EmptyState.IsLoadingText("No tweaks match. Try another word…"), "a sentence is not a loading text");
        T.True(!EmptyState.IsLoadingText(null) && !EmptyState.IsLoadingText("No settings to show."), "plain text is not loading");
        T.Eq(TweakFilter.ChipShort.Length, TweakFilter.ChipText.Length, "one short label per chip");
        foreach (var x in TweakFilter.ChipShort) T.True(L.Active || x.Length <= 12, "short chip label: " + x);
    }

    [Test] public static void Nav_PageFilter()
    {
        T.True(MainForm.NavMatch("Power tuner", ""), "empty filter shows all");
        T.True(MainForm.NavMatch("Power tuner", "tun"), "part of the name");
        T.True(MainForm.NavMatch("Power tuner", "  POWER "), "case and spaces ignored");
        T.False(MainForm.NavMatch("Power tuner", "latency"), "no match hidden");
    }

    [Test] public static void Nav_TweaksStartClosedInPro()
    {
        T.True(MainForm.DefaultOpen("Tweaks", false) && MainForm.DefaultOpen("Diagnostics", true), "Lite and other sections start open");
        T.False(MainForm.DefaultOpen("Tweaks", true), "Pro: Tweaks starts closed");
    }

    [Test] public static void WhatsNew_CategoryFilter()
    {
        string md = "## 2.0: b\n\nIntro.\n\n### Fixes\n- f1\n  - sub\n\n### Design\n- d1\n\n## 1.0: a\n\n### Design\n- d2\n- d3\n";
        string fx = WhatsNew.Only(md, "Fixes");
        T.True(fx.Contains("## 2.0: b") && fx.Contains("- f1") && fx.Contains("  - sub") && !fx.Contains("d1") && !fx.Contains("## 1.0") && !fx.Contains("Intro"), "one category, its versions only");
        T.Eq(WhatsNew.Count(md, "Design"), 3, "design count across versions");
        T.Eq(WhatsNew.Count(md, null), 4, "all changes");
        T.Eq(WhatsNew.Only(md, null), md, "all = unchanged");
    }

    [Test] public static void Changelog_EveryHeadingIsACategory()
    {
        var bad = Changelog.All.Replace("\r", "").Split('\n').Where(l => l.StartsWith("### ") && !WhatsNew.Cats.Contains(l.Substring(4).Trim())).ToList();
        T.True(bad.Count == 0, "unknown category heading: " + string.Join(", ", bad.ToArray()));
        int sum = WhatsNew.Cats.Sum(c => WhatsNew.Count(Changelog.All, c));
        T.Eq(sum, WhatsNew.Count(Changelog.All, null), "every change sits under a category heading");
    }

    // 3.37: the removed schemes open as Carbon dark (Blossom as Carbon light); the four kept keys stay
    [Test] public static void Design_RemovedSchemesMigrateAndLiteAccent()
    {
        foreach (var k in new[] { "slate", "rose", "perf", "strict", "fluent", "hud", "nonsense", "" }) T.Eq(Theme.SchemeFromKey(k), 0, "'" + k + "' opens as Carbon dark");
        T.Eq(Theme.SchemeFromKey("blossom"), 1, "Blossom (light) opens as Carbon light");
        T.Eq(Theme.SchemeFromKey("dark"), 0, "dark"); T.Eq(Theme.SchemeFromKey("light"), 1, "light"); T.Eq(Theme.SchemeFromKey("oled"), 2, "oled"); T.Eq(Theme.SchemeFromKey("custom"), Theme.CustomScheme, "custom");
        T.Eq(Theme.EffectiveAccent(Theme.CarbonRed, false), Theme.CarbonBlue, "Lite shows red as Carbon blue");
        T.Eq(Theme.EffectiveAccent(0, false), Theme.CarbonBlue, "Lite: the old red too");
        T.Eq(Theme.EffectiveAccent(Theme.CarbonRed, true), Theme.CarbonRed, "Pro keeps red");
        T.Eq(Theme.EffectiveAccent(2, false), 2, "other accents stay in Lite");
        T.Eq(Theme.AccentNames[Theme.WindowsAccent], "Windows", "Windows accent name"); T.True(Theme.SystemAccent().A == 255, "system accent readable");
        int keep = Theme.Scheme;
        try { foreach (var i in new[] { 0, 2 }) { Theme.SetScheme(i); T.True(Theme.Dark, Theme.SchemeNames[i] + " is dark"); T.True(Theme.Risk != Theme.Accent, "risk colour is not the accent"); } }
        finally { Theme.SetScheme(keep); }
    }

    // 3.35.1: e2e/test_smoke.py PAGES must list exactly the sidebar (3.35.0 missed 7 pages and named "Tools & backup")
    [Test] public static void E2ePageList_MatchesTheSidebar()
    {
        string root = TestHelp.RepoRoot();
        if (TestHelp.SkipWithoutSources(root)) return;
        string py = File.ReadAllText(Path.Combine(root, "e2e", "test_smoke.py"));
        var block = Regex.Match(py, @"PAGES = \[(.*?)\]", RegexOptions.Singleline);
        T.True(block.Success, "PAGES list found");
        var e2e = new HashSet<string>(Regex.Matches(block.Groups[1].Value, "\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value));
        string mf = File.ReadAllText(Path.Combine(root, "src", "Ui", "MainForm.cs"));
        var nav = new HashSet<string>(Regex.Matches(mf, @"AddNav\(nav, (?:null|""[^""]+""), ""([^""]+)""").Cast<Match>().Select(m => m.Groups[1].Value));
        foreach (var c in Catalog.Categories) nav.Add(c);
        var missing = nav.Where(n => !e2e.Contains(n)).OrderBy(n => n).ToArray();
        var extra = e2e.Where(n => !nav.Contains(n)).OrderBy(n => n).ToArray();
        T.Eq(missing.Length, 0, "sidebar pages missing from e2e PAGES: " + string.Join(", ", missing));
        T.Eq(extra.Length, 0, "e2e PAGES not in the sidebar: " + string.Join(", ", extra));
    }

    // 3.35.1: e2e PRO_PAGES (the only pages the smoke test may skip, in Lite mode) = Mode's Pro-only pages
    [Test] public static void E2eProPages_MatchMode()
    {
        string root = TestHelp.RepoRoot();
        if (TestHelp.SkipWithoutSources(root)) return;
        string py = File.ReadAllText(Path.Combine(root, "e2e", "test_smoke.py"));
        var block = Regex.Match(py, @"PRO_PAGES = \{(.*?)\}", RegexOptions.Singleline);
        T.True(block.Success, "PRO_PAGES set found");
        var e2e = new HashSet<string>(Regex.Matches(block.Groups[1].Value, "\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value));
        var mode = (HashSet<string>)typeof(Mode).GetField("ProPages", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        T.True(e2e.SetEquals(mode), "e2e PRO_PAGES and Mode.ProPages differ: e2e has " + string.Join(", ", e2e.Except(mode).ToArray()) + "; Mode has " + string.Join(", ", mode.Except(e2e).ToArray()));
    }

    // 3.37: eight accents; a saved accent from any earlier settings version lands on the same colour
    [Test] public static void Accents_SavedChoicesMigrate()
    {
        T.Eq(Theme.AccentNames.Length, 8, "eight accents");
        T.Eq(Theme.AccentNames[Theme.CarbonRed], "Red", "Red is Carbon red"); T.Eq(Theme.AccentNames[Theme.CarbonBlue], "Blue", "Blue is Carbon blue");
        T.Eq(Theme.AccentNames[Theme.NeonCyan], "Neon cyan", "neon cyan"); T.Eq(Theme.AccentNames[Theme.WindowsAccent], "Windows", "Windows"); T.Eq(Theme.AccentNames[Theme.PinkAccent], "Pink", "pink");
        int custom = Theme.AccentNames.Length - 1;
        // 3.29–3.36 order (AccentV 3): Red, Blue, Teal, Graphite, Pink, Carbon red, Carbon blue, Neon cyan, Windows, Custom
        int[] want = { Theme.CarbonRed, Theme.CarbonBlue, 2, 3, Theme.PinkAccent, Theme.CarbonRed, Theme.CarbonBlue, Theme.NeonCyan, Theme.WindowsAccent, custom };
        for (int i = 0; i < want.Length; i++) T.Eq(Theme.AccentFromSaved(i, "3"), want[i], "v3 accent " + i);
        T.Eq(Theme.AccentFromSaved(4, null), custom, "before 3.10 Custom was 4");
        T.Eq(Theme.AccentFromSaved(5, "2"), custom, "3.10–3.28 Custom was 5");
        T.Eq(Theme.AccentFromSaved(2, "2"), 2, "3.10–3.28 Teal stays Teal");
        for (int i = 0; i < Theme.AccentNames.Length; i++) T.Eq(Theme.AccentFromSaved(i, "4"), i, "v4 kept " + i);
        T.Eq(Theme.AccentFromSaved(42, "4"), Theme.CarbonRed, "out of range: red");
        T.Eq(Theme.AccentFromSaved(-1, "3"), Theme.CarbonRed, "negative: red");
    }
}
