// Carbon design tokens (colours, fonts, DPI scaling) and the two shared chrome controls. See docs/DESIGN.md.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    public static partial class Theme
    {
        public static bool Dark = true;
        public static Color Bg, Panel, PanelHover, Side, Accent, AccentDark, OnAccent, Border, PendingBg, Text, Muted, Ok, Warn, Bad, Grid, Sel, AltRow, LogBg, LogFg, AccentOff, Strong;
        // 3.29: high risk (Advanced, Danger zone) has its own colour, Carbon magenta, so the brand red never means "danger"
        public static Color Risk;
        public static Color Exp;   // 3.29: Experimental chips, Carbon purple
        // 3.36 depth tokens, derived from the scheme in Derive() (never set by hand): Line = hairline between and around
        // surfaces, Raised = a card on the page background, Hi = the 1 px light edge on top of a raised surface (dark only),
        // AccentSoft = a quiet accent tint for the one thing that needs attention, ShadowA = shadow strength (alpha)
        public static Color Line, Raised, Hi, AccentSoft, AccentInk; public static int ShadowA;   // AccentInk: the accent as small text, ≥ 4.5:1 on Panel

        // ---- Carbon design system (2.4.1): one token set; every page reads only these tokens ----
        public const int Radius = 4;        // one corner radius for cards/tiles (Carbon allows 0–4 px)
        // 3.11: Carbon spacing scale, 96-dpi pixels (spacing-01 … spacing-09). Layout uses only these, through S().
        public static class Sp { public const int X2 = 2, X4 = 4, X8 = 8, X12 = 12, X16 = 16, X24 = 24, X32 = 32, X40 = 40, X48 = 48; }
        // 3.11: Carbon productive motion durations, ms. Nothing in the app animates longer than Large.
        public static class Ms { public const int Micro = 70, Toggle = 110, Small = 150, Expand = 240, Large = 400; }
        // 3.11: readable line length for cards and tweak lists (Carbon "lg" column, Windows Settings ~1000–1100 px)
        public const int MaxContent = 1040;

        static Theme()
        {
            object o = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "Theme");
            object a = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "Accent");
            object av = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "AccentV");
            int ai; if (a != null && int.TryParse(a.ToString(), out ai)) AccentIndex = AccentFromSaved(ai, av == null ? null : av.ToString());
            if (a == null) AccentIndex = CarbonRed;   // a new install: Carbon dark with Carbon red
            object pr = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "Progress");
            int pri; if (pr != null && int.TryParse(pr.ToString(), out pri) && pri >= 0 && pri < ProgressNames.Length) ProgressIndex = pri;
            object pc = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "ProgressCustom");
            if (pc != null) { try { ProgressCustom = ColorTranslator.FromHtml(pc.ToString()); } catch { } }
            object cc = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "AccentCustom");
            // the old default (violet) was saved by every install; it only counts if Custom is the accent actually chosen
            if (cc != null && !(cc.ToString().Equals("#8B5CF6", StringComparison.OrdinalIgnoreCase) && AccentIndex != AccentNames.Length - 1))
            { try { CustomColor = ColorTranslator.FromHtml(cc.ToString()); } catch { } }
            object cm = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "Compact");
            Compact = cm != null && cm.ToString() == "1";
            object sl = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "Slim");
            Slim = sl != null && sl.ToString() == "1";
            object fw = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "FollowWindows");
            FollowWindows = fw != null && fw.ToString() == "1";
            object ts = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "TextSize");
            int tsi; if (ts != null && int.TryParse(ts.ToString(), out tsi) && tsi >= 0 && tsi < TextSizes.Length) TextSize = tsi;
            object scc = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "SchemeCustom");
            if (scc != null) { try { SchemeCustom = ColorTranslator.FromHtml(scc.ToString()); } catch { } }
            int si = o == null ? 0 : SchemeFromKey(o.ToString());
            // 3.29: once, the untouched old default red accent becomes Carbon red
            if (Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "DesignV") == null)
            {
                if (si == 0 && AccentIndex == 0) AccentIndex = CarbonRed;
                try { Reg.Set(Hive.HKCU, @"Software\ArchTweaker", "DesignV", Microsoft.Win32.RegistryValueKind.String, "1"); } catch { }
            }
            if (IsDarkScheme(si)) lastDark = si;
            object ds = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "DarkScheme");   // 3.2: the dark choice survives a light period
            int dsi = ds == null ? -1 : SchemeFromKey(ds.ToString());
            if (dsi >= 0 && IsDarkScheme(dsi)) lastDark = dsi;
            if (!IsDarkScheme(si)) lastLight = si;
            object ls = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "LightScheme");   // 3.10: the light choice survives a dark period
            int lsi = ls == null ? -1 : SchemeFromKey(ls.ToString());
            if (lsi >= 0 && !IsDarkScheme(lsi)) lastLight = lsi;
            SetScheme(FollowWindows ? FollowScheme(si) : si);
        }

        // ---- accent colours (2.9). Red is Carbon's own; the others were checked with docs/palettes.py against the same
        // rules (text on accent ≥ 4.5:1, accent on every surface ≥ 3:1, text on the tinted pending/selection rows ≥ 4.5:1).
        // Per accent and theme: Accent, AccentDark (hover), OnAccent, PendingBg, Sel, LogFg.
        // 3.37: eight accents. The old Red (#E11D2E) and Blue (#2F6FEB) were near copies of Carbon red and Carbon blue,
        // which now carry the names Red and Blue. Saved accents move with AccentFromSaved; AccentV "4" marks this order.
        public static readonly string[] AccentNames = { "Red", "Blue", "Teal", "Graphite", "Pink", "Neon cyan", "Windows", "Custom" };
        // a saved accent number in the order of its settings version → today's order
        public static int AccentFromSaved(int saved, string version)
        {
            int v3 = saved;
            if (version == null && saved == 4) v3 = 9;            // before 3.10 Custom was 4
            else if (version == "2" && saved == 5) v3 = 9;        // 3.10–3.28: Custom was 5
            else if (version == "4") return saved >= 0 && saved < AccentNames.Length ? saved : CarbonRed;
            // 3.29–3.36 order: Red, Blue, Teal, Graphite, Pink, Carbon red, Carbon blue, Neon cyan, Windows, Custom
            int[] map = { CarbonRed, CarbonBlue, 2, 3, PinkAccent, CarbonRed, CarbonBlue, NeonCyan, WindowsAccent, AccentNames.Length - 1 };
            return v3 >= 0 && v3 < map.Length ? map[v3] : CarbonRed;
        }   // 3.29: Carbon red/blue, Neon cyan (Gaming HUD), Windows = the system accent colour   // Custom (3.0) = AccentCustom, made readable by AccentMaker   // no amber: it would read as the Warn colour
        public static int AccentIndex;
        public static bool Compact;     // compact tweak rows: descriptions only on the selected row
        public static bool Slim;        // 3.2: icon-only sidebar
        // 3.2: text size on top of the display scale (the layout grows with the text where it measures it)
        public static readonly string[] TextSizeNames = { "100 %", "115 %", "130 %" };
        static readonly float[] TextSizes = { 1f, 1.15f, 1.3f };
        public static int TextSize;
        public static void SetTextSize(int i) { if (i < 0 || i >= TextSizes.Length) return; TextSize = i; Fonts(); }

        // 3.2: follow Windows' own app mode (Settings → Personalization → Colors). Light → Carbon light,
        // dark → the last dark scheme used here.
        public static bool FollowWindows;
        public static bool WindowsLight()
        {
            object v = Reg.Get(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme");
            return v is int && (int)v == 1;
        }
        public static int FollowScheme(int current) { return WindowsLight() ? (IsDarkScheme(current) ? lastLight : current) : (IsDarkScheme(current) ? current : lastDark); }
        // 3.2: a Windows high-contrast theme replaces every token with the system colours the person chose
        public static bool HighContrast;
        static void HighContrastColours()
        {
            HighContrast = false;
            try { HighContrast = SystemInformation.HighContrast; } catch { }
            if (!HighContrast) return;
            Color win = SystemColors.Window, txt = SystemColors.WindowText, hi = SystemColors.Highlight, hiT = SystemColors.HighlightText, gray = SystemColors.GrayText;
            Dark = win.GetBrightness() < 0.5f;
            Bg = Panel = PanelHover = Side = AltRow = LogBg = PendingBg = win;
            Text = Strong = Muted = LogFg = txt;
            Border = Grid = gray; AccentOff = gray;
            Accent = AccentDark = hi; OnAccent = hiT; Sel = hi;
            Ok = Warn = Bad = Risk = Exp = txt;
            On = OnHover = hi; OnInk = hiT;
            Derive();
        }
        // 3.36: the depth tokens from the current colours. High contrast: plain system colours, no shadow, no highlight.
        static void Derive()
        {
            if (HighContrast) { Line = Border; Raised = Panel; Hi = Panel; AccentSoft = Panel; AccentInk = Accent; ShadowA = 0; return; }
            Line = Gfx.Mix(Panel, Text, Dark ? 0.11 : 0.14);
            Raised = Panel;   // the same fill as Panel, so child controls on a card never show a seam; depth = shadow + edge
            Hi = Dark ? Gfx.Mix(Raised, Color.White, 0.075) : Panel;
            AccentSoft = Gfx.Mix(Panel, Accent, Dark ? 0.16 : 0.09);
            ShadowA = Dark ? 110 : 34;
            AccentInk = Accent;
            for (int i = 0; i < 20 && AccentMaker.Contrast(AccentInk, Panel) < 4.5; i++) AccentInk = Gfx.Mix(AccentInk, Strong, 0.08);
        }
        public const int CarbonRed = 0, CarbonBlue = 1, NeonCyan = 5, WindowsAccent = 6;
        // 3.29: the Windows accent colour (Settings → Personalization → Colors), or Windows blue
        public static Color SystemAccent()
        {
            try { object v = Reg.Get(Hive.HKCU, @"Software\Microsoft\Windows\DWM", "AccentColor"); if (v is int) { uint u = unchecked((uint)(int)v); return Color.FromArgb((int)(u & 0xFF), (int)((u >> 8) & 0xFF), (int)((u >> 16) & 0xFF)); } }   // ABGR
            catch { }
            return Color.FromArgb(0x00, 0x78, 0xD4);
        }
        // 3.29: Lite mode shows a red accent as Carbon blue; red is for Pro, where the expert tweaks are
        public static bool LiteNeutral = true;
        public static int EffectiveAccent(int chosen, bool pro) { return LiteNeutral && !pro && (chosen == 0 || chosen == CarbonRed) ? CarbonBlue : chosen; }
        public static Color AccentSwatch(int i) { return i == WindowsAccent ? SystemAccent() : i >= AccentSets.Length ? CustomColor : C(AccentSets[i][Dark ? 0 : 6]); }
        public static Color On, OnHover, OnInk;   // switched-on toggles, applied rows, fully tuned categories

        // ---- 3.10: progress bars (sidebar page tracks, Home category cards) get their own colour.
        // Green is the status colour (default); Accent follows the accent; the rest are fixed, checked ≥ 3:1 on every panel.
        public static readonly string[] ProgressNames = { "Green", "Accent", "Pink", "Blue", "Purple", "Custom" };
        static readonly string[][] ProgressSets = { null, null, new[] { "#F472B6", "#DB2777" }, new[] { "#60A5FA", "#2563EB" }, new[] { "#A78BFA", "#7C3AED" } };
        public static int ProgressIndex;
        public static readonly Color DefaultProgressCustom = Color.FromArgb(0xF4, 0x72, 0xB6);
        public static Color ProgressCustom = DefaultProgressCustom;
        public static Color Progress;
        public static Color ProgressColour(int i, bool dark, Color on, Color accent, Color panel, Color text)
        {
            if (i <= 0 || i >= ProgressNames.Length) return on;
            if (i == 1) return accent;
            Color c = i < ProgressSets.Length ? C(ProgressSets[i][dark ? 0 : 1]) : ProgressCustom;
            // a custom colour too close to the card is moved toward the text colour until the bar stands out (≥ 3:1)
            for (int k = 0; k < 20 && AccentMaker.Contrast(c, panel) < 3.0; k++) c = Gfx.Mix(c, text, 0.1);
            return c;
        }
        public static Color ProgressSwatch(int i) { return ProgressColour(i, Dark, On, Accent, Panel, Text); }
        public static readonly Color DefaultCustom = Color.FromArgb(0xC2, 0x41, 0x0C);   // 3.3: copper: clear of red brand, green On, amber Warn
        public static Color CustomColor = DefaultCustom;

        public static void Save()
        {
            using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(@"Software\ArchTweaker"))
            {
                k.SetValue("Theme", SchemeKeys[Scheme]);
                k.SetValue("Accent", AccentIndex.ToString());
                k.SetValue("AccentV", "4");   // 3.37: eight accents
                k.SetValue("Progress", ProgressIndex.ToString());
                k.SetValue("ProgressCustom", AccentMaker.Hex(ProgressCustom));
                k.SetValue("LightScheme", SchemeKeys[lastLight]);
                k.SetValue("SchemeCustom", AccentMaker.Hex(SchemeCustom));
                k.SetValue("AccentCustom", AccentMaker.Hex(CustomColor));
                k.SetValue("Compact", Compact ? "1" : "0");
                k.SetValue("Slim", Slim ? "1" : "0");
                k.SetValue("FollowWindows", FollowWindows ? "1" : "0");
                k.SetValue("TextSize", TextSize.ToString());
                k.SetValue("DarkScheme", SchemeKeys[lastDark]);
            }
        }

        static Color C(string hex) { return ColorTranslator.FromHtml(hex); }


        // ---- colour schemes (2.10). Every dark scheme uses the dark accent tints; Light uses the light ones.
        // 3.37: four schemes. Slate, Rose, Blossom and the four 3.29 design packages (Carbon Performance, Carbon Strict,
        // Fluent Native, Gaming HUD) were near copies of Carbon dark with another accent, which the accent picker already
        // does. A saved removed scheme opens as Carbon dark (Blossom, the light one, as Carbon light); the accent stays.
        public static readonly string[] SchemeNames = { "Carbon dark", "Carbon light", "OLED black", "Custom" };
        static readonly string[] SchemeKeys = { "dark", "light", "oled", "custom" };
        public static int SchemeFromKey(string key)
        {
            int i = Array.IndexOf(SchemeKeys, key ?? "");
            if (i >= 0) return i;
            return key == "blossom" ? 1 : 0;   // the removed light scheme → Carbon light; every other unknown key → Carbon dark
        }
        // 3.10.2: Custom = one background colour you pick; SchemeMaker builds the rest of the palette from it
        public const int CustomScheme = 3;
        public static readonly Color DefaultSchemeCustom = Color.FromArgb(0x1A, 0x1B, 0x2E);
        public static Color SchemeCustom = DefaultSchemeCustom;   // the colour picked
        public static Color SchemeCustomUsed;                     // the background after readability adjustments
        public static int Scheme;
        static int lastDark;        // the dark scheme the header Dark/Light switch returns to
        static int lastLight = 1;   // 3.10: and the light one
        public static bool IsDarkScheme(int i) { return i == CustomScheme ? SchemeMaker.IsDark(SchemeCustom) : i != 1; }
        public const int PinkAccent = 4;

        // Header switch: the last light scheme, or back to the last dark scheme used.
        public static void Set(bool dark)
        {
            if (dark) SetScheme(IsDarkScheme(Scheme) ? Scheme : lastDark);
            else SetScheme(!IsDarkScheme(Scheme) ? Scheme : lastLight);
        }

        public static void SetScheme(int scheme)
        {
            if (scheme < 0 || scheme >= SchemeNames.Length) scheme = 0;
            Scheme = scheme;
            if (IsDarkScheme(scheme)) lastDark = scheme; else lastLight = scheme;
            Colours(scheme);
            HighContrastColours();
            Derive();   // 3.36: again, now that HighContrast is current (turning it off at runtime left the HC depth tokens)
            Fonts();
        }

        // 3.10.4: the colours of a scheme / accent / progress choice without switching to it (Appearance preview, swatches)
        public struct Look { public bool Dark; public Color Bg, Panel, PanelHover, Side, Text, Muted, Accent, OnAccent, Progress, AccentOff, Border, Ok; }
        public static Look Preview(int scheme, int accent, int progress)
        {
            int a0 = AccentIndex, p0 = ProgressIndex;
            var keep = new Color[] { Bg, Panel, PanelHover, Side, Accent, AccentDark, OnAccent, Border, PendingBg, Text, Muted, Ok, Warn, Bad, Grid, Sel, AltRow, LogBg, LogFg, AccentOff, Strong, On, OnHover, OnInk, Progress, SchemeCustomUsed };
            bool d0 = Dark;
            try
            {
                AccentIndex = accent; ProgressIndex = progress; Colours(scheme < 0 || scheme >= SchemeNames.Length ? 0 : scheme);
                return new Look { Dark = Dark, Bg = Bg, Panel = Panel, PanelHover = PanelHover, Side = Side, Text = Text, Muted = Muted, Accent = Accent, OnAccent = OnAccent, Progress = Progress, AccentOff = AccentOff, Border = Border, Ok = Ok };
            }
            finally
            {
                AccentIndex = a0; ProgressIndex = p0; Dark = d0;
                Bg = keep[0]; Panel = keep[1]; PanelHover = keep[2]; Side = keep[3]; Accent = keep[4]; AccentDark = keep[5]; OnAccent = keep[6]; Border = keep[7]; PendingBg = keep[8]; Text = keep[9];
                Muted = keep[10]; Ok = keep[11]; Warn = keep[12]; Bad = keep[13]; Grid = keep[14]; Sel = keep[15]; AltRow = keep[16]; LogBg = keep[17]; LogFg = keep[18]; AccentOff = keep[19];
                Strong = keep[20]; On = keep[21]; OnHover = keep[22]; OnInk = keep[23]; Progress = keep[24]; SchemeCustomUsed = keep[25];
                Risk = d0 ? C("#FF7EB6") : C("#9F1853"); Exp = d0 ? C("#BE95FF") : C("#6929C4");
                Derive();   // 3.36: the depth tokens follow the restored colours
            }
        }

        // every colour token for one scheme with the current accent and progress choice
        static void Colours(int scheme)
        {
            bool dark = IsDarkScheme(scheme);
            Dark = dark;
            bool customDark; Color used;
            var c = scheme == CustomScheme ? SchemeMaker.Palette(SchemeCustom, out customDark, out used) : Palettes[scheme];
            SchemeCustomUsed = scheme == CustomScheme ? C(c[0]) : SchemeCustom;
            Bg = C(c[0]); Panel = C(c[1]); PanelHover = C(c[2]); Side = C(c[3]); Accent = C(c[4]); AccentDark = C(c[5]); OnAccent = C(c[6]);
            Border = C(c[7]); PendingBg = C(c[8]); Text = C(c[9]); Muted = C(c[10]); Strong = C(c[11]); Ok = C(c[12]); Warn = C(c[13]); Bad = C(c[14]);
            Grid = C(c[15]); Sel = C(c[16]); AltRow = C(c[17]); LogBg = C(c[18]); LogFg = C(c[19]); AccentOff = C(c[20]);
            string[] ac; int o;
            int ai = EffectiveAccent(AccentIndex, Mode.Pro);
            if (ai == WindowsAccent) { ac = AccentMaker.Make(SystemAccent(), dark, Bg, Panel, Side, Text); o = 0; }   // 3.29: made readable on these surfaces
            else if (ai >= AccentSets.Length) { ac = AccentMaker.Make(CustomColor, dark, Bg, Panel, Side, Text); o = 0; }
            else if (scheme == CustomScheme) { ac = AccentMaker.Make(C(AccentSets[ai][dark ? 0 : 6]), dark, Bg, Panel, Side, Text); o = 0; }   // 3.10.2: fitted to the custom surfaces
            else { ac = AccentSets[ai]; o = dark ? 0 : 6; }
            Accent = C(ac[o]); AccentDark = C(ac[o + 1]); OnAccent = C(ac[o + 2]); PendingBg = C(ac[o + 3]); Sel = C(ac[o + 4]); LogFg = C(ac[o + 5]);
            if (scheme == CustomScheme)
            {   // 3.10.2: muted text and the log colour must also stay readable on the derived tints
                for (int i = 0; i < 20 && (AccentMaker.Contrast(Muted, PendingBg) < 4.5 || AccentMaker.Contrast(Warn, PendingBg) < 3); i++) PendingBg = Gfx.Mix(PendingBg, Panel, 0.2);
                for (int i = 0; i < 30 && AccentMaker.Contrast(LogFg, LogBg) < 4.5; i++) LogFg = Gfx.Mix(LogFg, Text, 0.1);
            }
            // 3.1: "on / applied" state uses the status green, so red stays for the brand and the main action
            On = Ok; OnHover = Gfx.Mix(Ok, dark ? Color.White : Color.Black, 0.16);
            OnInk = AccentMaker.Contrast(Color.White, On) >= 4.5 ? Color.White : Color.FromArgb(11, 11, 12);
            Progress = ProgressColour(ProgressIndex, dark, On, Accent, Panel, Text);
            Risk = dark ? C("#FF7EB6") : C("#9F1853");   // Carbon Magenta 40 / 70: ≥ 3:1 on every panel
            Exp = dark ? C("#BE95FF") : C("#6929C4");    // Carbon Purple 40 / 70
            Derive();
        }

        // ---- one type system: every font in the app comes from here ----
        // 3.11: Windows 11's Segoe UI Variable (Text optical size) first; Segoe UI on Windows 10 or a stripped image
        static string[] BodyFaces() { return new[] { "Segoe UI Variable Text", "Segoe UI", "Inter" }; }
        static string[] HeadFaces() { return new[] { "Segoe UI Variable Text Semibold", "Segoe UI Semibold", "Inter SemiBold", "Inter" }; }
        static string Pick(string[] faces)
        {
            var have = new System.Collections.Generic.HashSet<string>(System.Drawing.FontFamily.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var f in faces) if (have.Contains(f)) return f;
            return "Segoe UI";
        }
        static string body = "Segoe UI", head = "Segoe UI Semibold", display = "Segoe UI Semibold";
        // 3.3: Windows 11's Segoe UI Variable Display (optical size for big type) for headings of 14 pt and up
        static bool headBold;   // a fallback face without a semibold cut is drawn bold instead
        static void Fonts()
        {
            body = Pick(BodyFaces()); head = Pick(HeadFaces()); fontCache.Clear();
            display = Pick(new[] { "Segoe UI Variable Display Semibold", "Segoe UI Variable Display Semib", head });
            headBold = head == body || head.IndexOf("Semi", StringComparison.OrdinalIgnoreCase) < 0 && head.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) < 0;
            // 3.11 type ramp (pt at 96 dpi): caption 9 = 12 px and strong 10.5 = 14 px are the Windows minimums for
            // Regular and Semibold. Below them Cyrillic text breaks up. Tags stay small caps at 8.5 pt semibold.
            F = Body(9.5f);
            FB = Semi(10.5f);
            FH = Semi(15f);
            FS = Body(9f);
            FT = Semi(8.5f);
            Mono = Cached(Pick(new[] { "Cascadia Mono", "Consolas", "DejaVu Sans Mono" }), 9.5f, FontStyle.Regular);
            string logo = Pick(new[] { "Segoe UI Black", "Inter" });
            LogoFont = Cached(logo, 15f / TextSizes[TextSize], logo == "Segoe UI Black" ? FontStyle.Regular : FontStyle.Bold);   // the logo keeps its size
        }

        // ---- DPI (2.6). Layout is written in 96-dpi pixels; S() converts. Fonts are given in points at 96 dpi and
        // scaled to the window's monitor (GDI turns points into pixels with the *system* DPI, so FontScale corrects
        // for a monitor whose DPI differs from it). A DPI change rebuilds the window (MainForm.WndProc).
        public static int Dpi = SystemDpi();
        public static float Scale { get { return Dpi / 96f; } }
        static float FontScale { get { return Dpi / (float)SystemDpi(); } }
        public static int S(int px) { return (int)Math.Round(px * Dpi / 96.0); }
        public static float S(float px) { return px * Dpi / 96f; }
        public static Size S(int w, int h) { return new Size(S(w), S(h)); }
        public static Padding S(Padding p) { return new Padding(S(p.Left), S(p.Top), S(p.Right), S(p.Bottom)); }
        static int sysDpi;
        static int SystemDpi()
        {
            if (sysDpi > 0) return sysDpi;
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) sysDpi = (int)Math.Round(g.DpiX); } catch { }
            if (sysDpi <= 0) sysDpi = 96;
            return sysDpi;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
        public static int WindowDpi(Control c)
        {
            try { if (Environment.OSVersion.Version >= new Version(10, 0, 14393)) { uint d = GetDpiForWindow(c.Handle); if (d > 0) return (int)d; } }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
            return SystemDpi();
        }
        public static void SetDpi(int dpi) { if (dpi < 48 || dpi > 960 || dpi == Dpi) return; Dpi = dpi; Fonts(); }

        // Scales a control tree built in 96-dpi pixels. Controls that lay themselves out in S() units opt out
        // through IDpiSelfScaled. ListView / DataGridView column widths are not covered by Control.Scale.
        public interface IDpiSelfScaled { }
        public static void ScaleTree(Control root)
        {
            if (Dpi == 96 || root is IDpiSelfScaled) return;
            var f = new SizeF(Scale, Scale);
            root.Scale(f);
            FixColumns(root);
        }
        static void FixColumns(Control c)
        {
            var lv = c as ListView;
            if (lv != null) foreach (ColumnHeader h in lv.Columns) if (h.Width > 0) h.Width = S(h.Width);
            var dg = c as DataGridView;
            if (dg != null)
            {
                foreach (DataGridViewColumn col in dg.Columns) { col.MinimumWidth = Math.Max(2, S(col.MinimumWidth)); if (col.AutoSizeMode == DataGridViewAutoSizeColumnMode.None || col.AutoSizeMode == DataGridViewAutoSizeColumnMode.NotSet) col.Width = S(col.Width); }
                dg.RowTemplate.Height = S(dg.RowTemplate.Height);
                if (dg.ColumnHeadersHeightSizeMode != DataGridViewColumnHeadersHeightSizeMode.AutoSize) dg.ColumnHeadersHeight = S(dg.ColumnHeadersHeight);
            }
            if (c is Theme.IDpiSelfScaled) return;
            foreach (Control k in c.Controls) FixColumns(k);
        }
        // fonts are cached per size: pages and OnPaint handlers ask for them freely without leaking GDI handles
        static readonly System.Collections.Generic.Dictionary<string, Font> fontCache = new System.Collections.Generic.Dictionary<string, Font>();
        static Font Cached(string face, float size, FontStyle st)
        {
            string key = face + "|" + size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (int)st;
            Font f; if (!fontCache.TryGetValue(key, out f)) { f = new Font(face, size * FontScale * TextSizes[TextSize], st); fontCache[key] = f; }
            return f;
        }
        public static Font Semi(float size)
        {
            if (size >= 14f && display != head) return Cached(display, size, FontStyle.Regular);
            return Cached(head, size, headBold ? FontStyle.Bold : FontStyle.Regular);
        }
        public static Font Body(float size) { return Cached(body, size, FontStyle.Regular); }
        // 3.36: read-out numbers (scores, counts, measurements). Bahnschrift is the DIN-style face Windows 10 1709+ ships;
        // its figures are even-width, so numbers that change don't jump. Segoe UI Variable Display, then the head face.
        static string numFace;
        public static Font Num(float size)
        {
            if (numFace == null) numFace = Pick(new[] { "Bahnschrift SemiBold", "Bahnschrift", "Segoe UI Variable Display Semibold", "Inter SemiBold", head });
            // GDI+ throws when a family lacks the asked style; then the face is dropped for the head face
            try { return Cached(numFace, size, numFace == "Bahnschrift" || numFace == "Inter" ? FontStyle.Bold : FontStyle.Regular); }
            catch (ArgumentException) { numFace = head; return Semi(size); }
        }
        // 3.36: small labels over read-outs ("HEALTH SCORE"): caption size, semibold, muted. Upper case only for Latin.
        public static string Cap(string s) { s = L.T(s); return L.Lang == "en" ? s.ToUpperInvariant() : s; }
        // the upper-case label when it fits in `width`, else the label as written (long labels stay readable)
        public static string CapFit(string s, Font f, int width)
        {
            string up = Cap(s);
            return TextRenderer.MeasureText(up, f, Size.Empty, TextFormatFlags.NoPrefix).Width <= width ? up : L.T(s);
        }
        public static Font BodyBold(float size) { return Cached(body, size, FontStyle.Bold); }

        public static Font F, FB, FH, FS, FT, Mono, LogoFont;

        // Native scrollbars, list headers and tree glyphs follow the theme (Windows 10 1809+ "DarkMode_Explorer").
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string id);
        public static void NativeTheme(Control root)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            foreach (Control c in root.Controls)
            {
                if (c is ScrollableControl || c is ListView || c is TreeView || c is DataGridView || (c is TextBoxBase && ((TextBoxBase)c).Multiline) || c is ListBox)
                {
                    Control cc = c;
                    EventHandler apply = delegate { try { Chrome.AllowDark(cc.Handle); SetWindowTheme(cc.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { } };
                    if (cc.IsHandleCreated) apply(cc, EventArgs.Empty); else cc.HandleCreated += apply;
                }
                if (c.HasChildren) NativeTheme(c);
            }
        }

        public static Button Btn(string text, EventHandler click)
        {
            var b = new FocusButton { Text = text, UseMnemonic = false, FlatStyle = FlatStyle.Flat, MinimumSize = new Size(0, 30), BackColor = Raised, ForeColor = Text, Font = F, AutoSize = true, Padding = new Padding(14, 4, 14, 4), Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Line;
            b.FlatAppearance.MouseOverBackColor = PanelHover;          // hover: one step
            b.FlatAppearance.MouseDownBackColor = Grid;                // press: a second step
            if (click != null) b.Click += click;
            b.EnabledChanged += delegate { if (b.BackColor != Accent) b.ForeColor = b.Enabled ? Text : Muted; };
            return b;
        }
        public static Button Primary(string text, EventHandler click)
        {
            var b = Btn(text, click); b.Font = FB;
            // disabled primary drops to the neutral surface so it never reads as an available red action
            EventHandler paint = delegate
            {
                b.BackColor = b.Enabled ? Accent : Panel; b.ForeColor = b.Enabled ? OnAccent : Muted;
                b.FlatAppearance.BorderColor = b.Enabled ? Accent : Border;
                b.FlatAppearance.MouseOverBackColor = b.Enabled ? AccentDark : Panel;
                b.FlatAppearance.MouseDownBackColor = b.Enabled ? Gfx.Mix(AccentDark, Bg, 0.25) : Panel;
            };
            paint(b, EventArgs.Empty); b.EnabledChanged += paint; return b;
        }
        // 3.14: the main action of a tool page. An accent outline, not a fill: the filled accent button is kept for
        // Apply and for the confirm button of a dialog, so a view never shows two of them.
        public static Button Lead(string text, EventHandler click)
        {
            var b = Btn(text, click); b.Font = FB;
            EventHandler paint = delegate
            {
                b.ForeColor = b.Enabled ? Strong : Muted;
                b.FlatAppearance.BorderColor = b.Enabled ? Accent : Line;
                b.FlatAppearance.BorderSize = b.Enabled ? Math.Max(2, S(2)) : 1;
            };
            paint(b, EventArgs.Empty); b.EnabledChanged += paint; return b;
        }
        public static FlowLayoutPanel Bar()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(0, 6, 0, 6), BackColor = Bg, WrapContents = false };
        }
        // vertical-only scrolling for row lists: the horizontal bar appeared for a moment-wide overflow while the
        // vertical bar was being added, and then stayed (2.8)
        public static void VerticalOnly(ScrollableControl c)
        {
            c.AutoScroll = false;
            c.HorizontalScroll.Enabled = false; c.HorizontalScroll.Visible = false; c.HorizontalScroll.Maximum = 0;
            c.AutoScroll = true;
        }
        public static void DarkList(ListView lv)
        {
            lv.BackColor = Panel; lv.ForeColor = Text; lv.Font = F; lv.BorderStyle = BorderStyle.None; lv.FullRowSelect = true; lv.View = View.Details;
            lv.OwnerDraw = true;
            lv.DrawColumnHeader += (s, e) => DrawHeader(e);
            Table.Attach(lv);   // 3.16
            Skin.FillLastColumn(lv);
        }
        // one list-header look for every ListView: Side background, Muted semibold text, thin separators
        public static void DrawHeader(DrawListViewColumnHeaderEventArgs e)
        {
            // 3.36: instrument-style header: a step below the panel, caption labels, one hairline under the row
            Color hb = Gfx.Mix(Panel, Bg, 0.5);
            using (var br = new SolidBrush(hb)) e.Graphics.FillRectangle(br, e.Bounds);
            using (var pen = new Pen(Line)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            var r = e.Bounds; r.X += S(8); r.Width -= S(12);
            var al = e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : e.Header.TextAlign == HorizontalAlignment.Center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
            TextRenderer.DrawText(e.Graphics, Cap(e.Header.Text), FT, r, Muted, hb, al | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        public static void DarkGrid(DataGridView g)
        {
            g.BackgroundColor = Panel; g.BorderStyle = BorderStyle.None; g.GridColor = Grid;
            g.EnableHeadersVisualStyles = false; g.RowHeadersVisible = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = Gfx.Mix(Panel, Bg, 0.5); g.ColumnHeadersDefaultCellStyle.ForeColor = Muted; g.ColumnHeadersDefaultCellStyle.Font = FT;   // 3.36: as the list headers
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = g.ColumnHeadersDefaultCellStyle.BackColor;
            g.DefaultCellStyle.BackColor = Panel; g.DefaultCellStyle.ForeColor = Text; g.DefaultCellStyle.Font = F;
            g.DefaultCellStyle.SelectionBackColor = Sel; g.DefaultCellStyle.SelectionForeColor = Text;
            g.AlternatingRowsDefaultCellStyle.BackColor = AltRow;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None; g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.AllowUserToAddRows = false; g.AllowUserToDeleteRows = false; g.AllowUserToResizeRows = false; g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.ColumnHeadersHeight = 32; g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; g.RowTemplate.Height = 28;
            Table.Attach(g);   // 3.16: 30 px rows, status pills
        }
        // Toolbar drop-down list (fixed items, no typing).
        public static ComboBox Drop(int width, string name, int right = 12)
        {
            return new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Margin = new Padding(0, 5, right, 0), AccessibleName = name };
        }
        // Bold status text at the end of a toolbar ("Round 2 of 3…"); never translated, the caller passes final text.
        public static Label StatusText()
        {
            return new Label { AutoSize = true, ForeColor = Text, Font = FB, Margin = new Padding(12, 8, 0, 0), UseMnemonic = false, Tag = "notr" };
        }
        // Sets a label's text from a worker thread; does nothing once the page is gone.
        public static Action<string> SayTo(Control owner, Label status)
        {
            return t => { if (owner.IsHandleCreated && !owner.IsDisposed) try { owner.BeginInvoke((Action)(() => status.Text = t)); } catch (InvalidOperationException) { } };
        }
        // Muted caption in front of a toolbar control ("Game:", "Rounds:").
        public static Label BarLabel(string text)
        {
            return new Label { Text = text, ForeColor = Muted, Font = FS, AutoSize = true, Margin = new Padding(0, 8, 4, 0) };
        }
        // 3.36.2: the note grows to fit its wrapped text (the fixed 34 px cut the second line at bigger text sizes)
        public static Label Note(string text)
        {
            var l = new Label { Text = text, ForeColor = Muted, Font = FS, Dock = DockStyle.Top, Height = 34, Padding = new Padding(2, 4, 0, 0) };
            EventHandler fit = delegate
            {
                if (l.IsDisposed || l.Width <= 0) return;
                int w = Math.Max(S(80), l.ClientSize.Width - l.Padding.Horizontal);
                int h = TextRenderer.MeasureText(L.T(l.Text ?? ""), l.Font, new Size(w, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + l.Padding.Vertical + S(6);
                if (Math.Abs(l.Height - h) > 1) l.Height = h;
            };
            l.Resize += fit; l.TextChanged += fit; l.FontChanged += fit;
            return l;
        }
    }

}
