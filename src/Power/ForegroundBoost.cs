// ArchTweaker: Win32PrioritySeparation picker (3.30): every valid quantum / boost value, the current one, and the Windows default.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Microsoft.Win32;

namespace ArchTweaker
{
    // Win32PrioritySeparation is 6 bits: bits 4-5 quantum length (1 long, 2 short), bits 2-3 quantum type
    // (1 variable, 2 fixed), bits 0-1 foreground boost (0 = 1:1, 1 = 2:1, 2 = 3:1).
    public static class ForegroundBoost
    {
        public const string Key = @"SYSTEM\CurrentControlSet\Control\PriorityControl", Name = "Win32PrioritySeparation";
        public const int WindowsDefault = 2;

        // the 12 values that set every field explicitly, short quanta first
        public static readonly int[] Values = { 0x24, 0x25, 0x26, 0x28, 0x29, 0x2A, 0x14, 0x15, 0x16, 0x18, 0x19, 0x1A };

        public static int? Current()
        {
            object o = Reg.Get(Hive.HKLM, Key, Name);
            return o is int ? (int?)(int)o : null;
        }
        public static void Set(int v)
        {
            if (v < 0 || v > 0x3F) throw new ArgumentOutOfRangeException("v");
            Reg.Set(Hive.HKLM, Key, Name, RegistryValueKind.DWord, v);   // journaled: Undo everything puts back the value before
            Log.W("Win32PrioritySeparation set to 0x{0:X2} ({1}).", v, Describe(v));
        }

        public static string Describe(int v)
        {
            int len = (v >> 4) & 3, kind = (v >> 2) & 3, boost = v & 3;
            if (len == 0 && kind == 0) return L.T("Windows default (short, variable quanta on desktop editions)") + ", " + Boost(boost);
            string l = len == 1 ? L.T("long quanta") : len == 2 ? L.T("short quanta") : L.T("default length");
            string k = kind == 1 ? L.T("variable") : kind == 2 ? L.T("fixed") : L.T("default type");
            return l + ", " + k + ", " + Boost(boost);
        }
        static string Boost(int b) { return b == 0 ? L.T("no foreground boost (1:1)") : b == 1 ? L.T("foreground boost 2:1") : L.T("foreground boost 3:1"); }
        // a variable quantum with a boost is what the foreground boost needs; with a fixed quantum the boost bits do nothing
        public static bool BoostWorks(int v) { int kind = (v >> 2) & 3; return kind != 2 && (v & 3) != 0; }
    }

}
