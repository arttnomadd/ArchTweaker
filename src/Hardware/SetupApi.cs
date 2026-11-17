// ArchTweaker: SetupAPI and CfgMgr32 imports shared by DevCtl (enable/disable) and Ghosts (remove).
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ArchTweaker
{
    static class SetupApi
    {
        [StructLayout(LayoutKind.Sequential)] public struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string enumerator, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] public static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA data, StringBuilder id, int size, out int req);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data, uint prop, out uint type, byte[] buf, int size, out int req);
        [DllImport("setupapi.dll", SetLastError = true)] public static extern bool SetupDiCallClassInstaller(uint dif, IntPtr set, ref SP_DEVINFO_DATA data);
        [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("cfgmgr32.dll")] public static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);

        public const int ERROR_NO_MORE_ITEMS = 259;

        // A string device property, or "" when the device has none.
        public static string Prop(IntPtr set, ref SP_DEVINFO_DATA d, uint prop)
        {
            uint type; int req; var buf = new byte[1024];
            if (!SetupDiGetDeviceRegistryPropertyW(set, ref d, prop, out type, buf, buf.Length, out req)) return "";
            return Encoding.Unicode.GetString(buf, 0, Math.Max(0, Math.Min(req, buf.Length) - 2)).TrimEnd('\0');
        }
    }
}
