// ArchTweaker: colour data for Theme (3.32 split from Theme.cs): accent sets and scheme palettes. docs/palettes.py checks these.
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
        // Per accent and theme: Accent, AccentDark (hover), OnAccent, PendingBg, Sel, LogFg (dark six, then light six).
        static readonly string[][] AccentSets =
        {
            new[] { "#DA1E28","#B81921","#FFFFFF","#2D0709","#520408","#FFB3B8",   "#DA1E28","#A2191F","#FFFFFF","#FFF1F1","#FFD7D9","#A2191F" },   // Red: Carbon Red 60 (3.37: the only red; the old #E11D2E Red was removed)
            new[] { "#0F62FE","#0353E9","#FFFFFF","#001141","#002D9C","#A6C8FF",   "#0F62FE","#0043CE","#FFFFFF","#EDF5FF","#D0E2FF","#0043CE" },   // Blue: Carbon Blue 60 (3.37: the only blue)
            new[] { "#14B8A6","#2DD4BF","#000000","#0D2E2B","#0E3B37","#95DFD7",   "#0F766E","#0B5C55","#FFFFFF","#E5F0EF","#D1E5E3","#0B5852" },
            new[] { "#E4E4E7","#C4C4CA","#0B0B0C","#29292B","#363638","#ECECEE",   "#27272A","#3F3F46","#FFFFFF","#E9E9EA","#DADADB","#27272A" },
            new[] { "#F472B6","#F9A8D4","#000000","#3A1530","#4A1A3D","#F2A7CF",   "#BE185D","#9D174D","#FFFFFF","#FCE7F3","#FBCFE8","#9D174D" },   // 3.10: Pink
            new[] { "#00D1FF","#33DBFF","#000000","#002B36","#00404F","#82E9FF",   "#0072C3","#00539A","#FFFFFF","#E5F6FF","#BAE6FF","#00539A" },   // 3.29: Neon cyan (Gaming HUD)
        };

        // Bg, Panel, PanelHover, Side, Accent, AccentDark(hover), OnAccent, Border, PendingBg, Text, Muted, Strong,
        // Ok, Warn, Bad, Grid, Sel, AltRow, LogBg, LogFg, AccentOff — all pairs checked to WCAG (text ≥ 7:1, muted ≥ 4.5:1).
        static readonly string[][] Palettes =
        {
            new[] { "#0B0B0C","#161618","#1F1F22","#070708","#E11D2E","#B5121F","#FFFFFF","#2A2A2E","#3A1418","#ECECEE","#A1A1A8","#FFFFFF","#34C759","#FFB020","#FF6B6B","#232327","#4A1219","#121214","#050506","#D9A0A4","#2E2E33" }, // Carbon dark
            new[] { "#F4F4F6","#FFFFFF","#F0F0F3","#EAEAEE","#C8102E","#9E0C24","#FFFFFF","#D9D9DE","#FDE3E6","#16161A","#5C5C66","#000000","#0E7434","#7E4A00","#AE1219","#E4E4E9","#FCD2D8","#F9F9FB","#FCFCFD","#8E1B29","#CFCFD6" }, // Carbon light
            new[] { "#000000","#0E0E10","#18181B","#000000","#E11D2E","#B5121F","#FFFFFF","#222226","#3A1418","#ECECEE","#A1A1A8","#FFFFFF","#34C759","#FFB020","#FF6B6B","#1A1A1E","#4A1219","#08080A","#000000","#D9A0A4","#2A2A2E" }, // OLED black: true black for OLED panels
        };
    }
}
