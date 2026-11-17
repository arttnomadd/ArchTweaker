// ArchTweaker: netsh TCP global settings.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Win32;

namespace ArchTweaker
{
}


namespace ArchTweaker
{
    // 3.6.2: TCP Fast Open state from netsh (no PowerShell cmdlet reports it). null = output not understood (non-English Windows).
    public static class NetshGlobal
    {
        static DateTime at; static bool? cached;
        public static bool? FastOpen()
        {
            if ((DateTime.Now - at).TotalSeconds < 15) return cached;
            at = DateTime.Now;
            string o = ""; try { o = Shell.Run("netsh.exe", "int tcp show global", true, 5000) ?? ""; } catch { }
            return cached = ParseFastOpen(o);
        }
        public static bool? ParseFastOpen(string netsh)
        {
            var m = System.Text.RegularExpressions.Regex.Match(netsh ?? "", @"^\s*Fast\s*Open\s*:\s*(\S+)", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            string v = m.Groups[1].Value.ToLowerInvariant();
            if (v == "enabled") return true;
            if (v == "disabled") return false;
            return null;
        }
    }
}
