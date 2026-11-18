// ArchTweaker: Builds accent colours and colour schemes from one chosen colour.
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
    // ================= CUSTOM ACCENT =================
    public static class AccentMaker
    {
        static double Lum(Color c)
        {
            Func<double, double> f = v => { v /= 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); };
            return 0.2126 * f(c.R) + 0.7152 * f(c.G) + 0.0722 * f(c.B);
        }
        public static double Contrast(Color a, Color b) { double x = Lum(a), y = Lum(b); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }
        public static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        // Accent, AccentDark (hover), OnAccent, PendingBg, Sel, LogFg — readable on the given surfaces.
        public static string[] Make(Color pick, bool dark, Color bg, Color panel, Color side, Color text)
        {
            Color toward = dark ? Color.White : Color.Black, acc = Color.FromArgb(pick.R, pick.G, pick.B);
            for (int i = 0; i < 40 && (Contrast(acc, bg) < 3 || Contrast(acc, panel) < 3 || Contrast(acc, side) < 3); i++) acc = Gfx.Mix(acc, toward, 0.06);
            Color white = Color.FromArgb(255, 255, 255), black = Color.FromArgb(11, 11, 12);
            Color on = Contrast(white, acc) >= 4.5 ? white : black;
            Color away = on == white ? Color.Black : Color.White;
            for (int i = 0; i < 30 && Contrast(on, acc) < 4.5; i++) acc = Gfx.Mix(acc, away, 0.05);   // keep button text readable
            Color hover = Gfx.Mix(acc, away, 0.16);
            for (int i = 0; i < 20 && Contrast(on, hover) < 4.5; i++) hover = Gfx.Mix(hover, away, 0.08);
            double t = dark ? 0.22 : 0.14;
            Color pend = Gfx.Mix(panel, acc, t), sel = Gfx.Mix(panel, acc, Math.Min(1, t * 1.4));
            for (int i = 0; i < 20 && (Contrast(text, pend) < 4.5 || Contrast(text, sel) < 4.5); i++) { t *= 0.8; pend = Gfx.Mix(panel, acc, t); sel = Gfx.Mix(panel, acc, t * 1.4); }
            Color log = Gfx.Mix(acc, text, 0.45);
            return new[] { Hex(acc), Hex(hover), Hex(on), Hex(pend), Hex(sel), Hex(log) };
        }
    }

    // ================= 3.10.2: CUSTOM COLOUR SCHEME =================
    // Builds a full palette (Theme.Palettes order) from one background colour. A dark pick gets light text, a light pick
    // dark text. If no text colour can reach 7:1 on a mid-tone pick, the background moves darker or lighter until it can.
    public static class SchemeMaker
    {
        static readonly Color White = Color.FromArgb(255, 255, 255), Black = Color.FromArgb(0, 0, 0);
        static Color M(Color a, Color b, double t) { return Gfx.Mix(a, b, t); }
        static double Cr(Color a, Color b) { return AccentMaker.Contrast(a, b); }
        // moves c toward 'to' until it reaches min contrast on every surface
        static Color Ensure(Color c, Color to, double min, params Color[] on)
        {
            for (int i = 0; i < 40 && on.Any(x => Cr(c, x) < min); i++) c = M(c, to, 0.08);
            return c;
        }
        public static bool IsDark(Color bg) { return Cr(White, bg) >= Cr(Black, bg); }

        public static string[] Palette(Color pick, out bool dark, out Color bg)
        {
            bg = Color.FromArgb(pick.R, pick.G, pick.B); dark = IsDark(bg);
            Color text, panel, hover, side, border, grid, alt, logBg, off, strong, muted, ok, warn, bad, logFg;
            if (dark)
            {
                text = Color.FromArgb(0xEC, 0xEC, 0xEE);
                for (int i = 0; i < 40 && Cr(text, M(bg, White, 0.09)) < 7; i++) bg = M(bg, Black, 0.06);
                panel = M(bg, White, 0.05); hover = M(bg, White, 0.09); side = M(bg, Black, 0.35); border = M(bg, White, 0.14);
                grid = M(bg, White, 0.10); alt = M(bg, White, 0.025); logBg = M(bg, Black, 0.5); off = M(bg, White, 0.16); strong = White;
                muted = Ensure(M(text, bg, 0.35), text, 4.5, bg, panel, hover, side);
                ok = Ensure(Color.FromArgb(0x34, 0xC7, 0x59), White, 4.5, panel, hover);
                warn = Ensure(Color.FromArgb(0xFF, 0xB0, 0x20), White, 4.5, panel, hover);
                bad = Ensure(Color.FromArgb(0xFF, 0x6B, 0x6B), White, 4.5, panel, hover);
            }
            else
            {
                text = Color.FromArgb(0x16, 0x16, 0x1A);
                for (int i = 0; i < 40 && Cr(text, bg) < 7; i++) bg = M(bg, White, 0.06);
                panel = M(bg, White, 0.7); hover = M(bg, White, 0.35); side = M(bg, Black, 0.04); border = M(bg, Black, 0.12);
                grid = M(bg, Black, 0.07); alt = M(panel, bg, 0.3); logBg = M(bg, White, 0.85); off = M(bg, Black, 0.15); strong = Black;
                for (int i = 0; i < 40 && Cr(text, side) < 7; i++) side = M(side, White, 0.1);
                muted = Ensure(M(text, bg, 0.45), text, 4.5, bg, panel, hover, side);
                ok = Ensure(Color.FromArgb(0x0E, 0x74, 0x34), Black, 4.5, panel, hover, bg);
                warn = Ensure(Color.FromArgb(0x7E, 0x4A, 0x00), Black, 4.5, panel, hover, bg);
                bad = Ensure(Color.FromArgb(0xAE, 0x12, 0x19), Black, 4.5, panel, hover, bg);
            }
            logFg = Ensure(M(text, bg, 0.2), text, 4.5, logBg);
            // accent slots (4-6, 8, 16) are placeholders: Theme fills them with AccentMaker for the chosen accent
            Color[] p = { bg, panel, hover, side, Color.Red, Color.Red, White, border, panel, text, muted, strong, ok, warn, bad, grid, panel, alt, logBg, logFg, off };
            return p.Select(AccentMaker.Hex).ToArray();
        }
    }
}
