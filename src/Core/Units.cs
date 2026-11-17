// ArchTweaker: sizes as text and System32 paths (3.32): one copy instead of one per page.
using System;
using System.IO;

namespace ArchTweaker
{
    public static class Units
    {
        // 1024-based, one decimal from KB up: "512 B", "1.5 KB", "3.2 GB"
        public static string Bytes(long b)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" }; double d = b; int i = 0;
            while (d >= 1024 && i < u.Length - 1) { d /= 1024; i++; }
            return (i == 0 ? d.ToString("0") : d.ToString("0.0")) + " " + u[i];
        }
        // whole MB, or GB with one decimal from 1024 MB
        public static string MbGb(long b) { double mb = b / 1048576.0; return mb >= 1024 ? (mb / 1024).ToString("0.0") + " GB" : mb.ToString("0") + " MB"; }
        // a tool in System32, by full path, so no other folder on PATH can supply it
        public static string Sys(string exe) { return Path.Combine(Environment.SystemDirectory, exe); }
    }
}
