// ArchTweaker: Win32 imports used by the core.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint action, uint param, int[] vparam, uint winIni);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint action, uint param, IntPtr vparam, uint winIni);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);
        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        public static void BroadcastSettingChange()
        {
            UIntPtr r;
            SendMessageTimeout((IntPtr)0xffff, 0x001A, UIntPtr.Zero, "ImmersiveColorSet", 2, 1000, out r);
            SendMessageTimeout((IntPtr)0xffff, 0x001A, UIntPtr.Zero, "Environment", 2, 1000, out r);
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }

        public static void SetMouseAccel(bool on)
        {
            // SPI_SETMOUSE: thresholds + acceleration flag, applied live without re-logon
            int[] v = on ? new[] { 6, 10, 1 } : new[] { 0, 0, 0 };
            SystemParametersInfo(0x0004, 0, v, 0x01 | 0x02);
        }
    }
}
