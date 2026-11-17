// ArchTweaker: Windows build helpers.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ---------------- Windows edition ----------------
    public static partial class OSInfo
    {
        public static string Edition { get { return (Reg.Get(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID") ?? "").ToString(); } }
        // 0 = Home, 1 = Pro, 2 = Enterprise / Education
        public static int EditionTier
        {
            get
            {
                string e = Edition;
                if (e.StartsWith("Core", StringComparison.OrdinalIgnoreCase)) return 0;
                if (e.StartsWith("Enterprise", StringComparison.OrdinalIgnoreCase) || e.StartsWith("Education", StringComparison.OrdinalIgnoreCase)
                    || e.StartsWith("IoTEnterprise", StringComparison.OrdinalIgnoreCase) || e.StartsWith("Server", StringComparison.OrdinalIgnoreCase)) return 2;
                return 1;
            }
        }
    }
}
