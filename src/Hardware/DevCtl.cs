// ArchTweaker: Device enable/disable through SetupAPI.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ---------------- devices: disable / enable through SetupAPI ----------------
    public class DevInfo { public string Id, Name; public bool Disabled; }

    public static class DevCtl
    {
        [StructLayout(LayoutKind.Sequential)] struct SP_CLASSINSTALL_HEADER { public uint cbSize; public uint InstallFunction; }
        [StructLayout(LayoutKind.Sequential)] struct SP_PROPCHANGE_PARAMS { public SP_CLASSINSTALL_HEADER Header; public uint StateChange; public uint Scope; public uint HwProfile; }
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiSetClassInstallParamsW(IntPtr set, ref SetupApi.SP_DEVINFO_DATA data, ref SP_PROPCHANGE_PARAMS p, int size);
        const uint DIGCF_PRESENT = 0x2, DIGCF_ALLCLASSES = 0x4, SPDRP_DEVICEDESC = 0x0, SPDRP_FRIENDLYNAME = 0xC, DIF_PROPERTYCHANGE = 0x12, DICS_ENABLE = 1, DICS_DISABLE = 2, DICS_FLAG_GLOBAL = 1;
        const uint DN_HAS_PROBLEM = 0x400, CM_PROB_DISABLED = 22;


        // Walks present devices; 'each' returns a DICS_* change to make (0 = none).
        // 3.27.1: returns the IDs whose change failed, and throws when the device list could not be read to the end.
        static List<string> Walk(Func<DevInfo, uint> each)
        {
            var failed = new List<string>();
            IntPtr set = SetupApi.SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
            if (set == new IntPtr(-1)) throw new InvalidOperationException("SetupDiGetClassDevs failed (" + Marshal.GetLastWin32Error() + ")");
            try
            {
                var d = new SetupApi.SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SetupApi.SP_DEVINFO_DATA)) };
                for (uint i = 0; SetupApi.SetupDiEnumDeviceInfo(set, i, ref d); i++)
                {
                    var sb = new StringBuilder(512); int req;
                    string id = SetupApi.SetupDiGetDeviceInstanceIdW(set, ref d, sb, sb.Capacity, out req) ? sb.ToString() : "";
                    uint st, pr; bool disabled = SetupApi.CM_Get_DevNode_Status(out st, out pr, d.DevInst, 0) == 0 && (st & DN_HAS_PROBLEM) != 0 && pr == CM_PROB_DISABLED;
                    string name = SetupApi.Prop(set, ref d, SPDRP_FRIENDLYNAME); if (name.Length == 0) name = SetupApi.Prop(set, ref d, SPDRP_DEVICEDESC);
                    uint change = each(new DevInfo { Id = id, Name = name, Disabled = disabled });
                    if (change != 0)
                    {
                        var p = new SP_PROPCHANGE_PARAMS { Header = new SP_CLASSINSTALL_HEADER { cbSize = (uint)Marshal.SizeOf(typeof(SP_CLASSINSTALL_HEADER)), InstallFunction = DIF_PROPERTYCHANGE }, StateChange = change, Scope = DICS_FLAG_GLOBAL, HwProfile = 0 };
                        if (!SetupDiSetClassInstallParamsW(set, ref d, ref p, Marshal.SizeOf(typeof(SP_PROPCHANGE_PARAMS))) || !SetupApi.SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, set, ref d))
                        { Log.W("  {0}: could not {1} ({2})", name, change == DICS_DISABLE ? "disable" : "enable", Marshal.GetLastWin32Error()); failed.Add(id); }
                        else Log.W("  {0}: {1}", name, change == DICS_DISABLE ? "disabled" : "enabled");
                    }
                    d = new SetupApi.SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SetupApi.SP_DEVINFO_DATA)) };
                }
                int last = Marshal.GetLastWin32Error();
                if (last != 0 && last != SetupApi.ERROR_NO_MORE_ITEMS) throw new InvalidOperationException("SetupDiEnumDeviceInfo stopped early (" + last + ")");
            }
            finally { SetupApi.SetupDiDestroyDeviceInfoList(set); }
            return failed;
        }

        public static bool Matches(DevInfo d, string[] idPrefixes, string[] names)
        {
            if (idPrefixes != null && idPrefixes.Any(p => (d.Id ?? "").StartsWith(p, StringComparison.OrdinalIgnoreCase))) return true;
            return names != null && names.Any(n => (d.Name ?? "").IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        public static List<DevInfo> Find(string[] idPrefixes, string[] names)
        {
            var l = new List<DevInfo>();
            Walk(d => { if (Matches(d, idPrefixes, names)) l.Add(d); return 0; });
            return l;
        }
        public static List<string> Change(string[] idPrefixes, string[] names, Func<DevInfo, bool> pick, bool disable)
        {
            return Walk(d => Matches(d, idPrefixes, names) && pick(d) ? (disable ? DICS_DISABLE : DICS_ENABLE) : 0);
        }
    }
}
