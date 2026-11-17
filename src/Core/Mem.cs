// ArchTweaker: Memory information (total, available).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static class Mem
    {
        [StructLayout(LayoutKind.Sequential)]
        class MEMORYSTATUSEX { public uint dwLength = 64; public uint dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);
        public static uint TotalKB()
        {
            try { var m = new MEMORYSTATUSEX(); if (GlobalMemoryStatusEx(m)) return (uint)Math.Min(uint.MaxValue, m.ullTotalPhys / 1024); } catch { }
            return 0;
        }
    }
}
