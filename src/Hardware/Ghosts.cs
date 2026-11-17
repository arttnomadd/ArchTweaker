// ArchTweaker: Ghost devices: devices Windows remembers but that are not connected, and their dialog.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ---------------- ghost devices: devices Windows still remembers but that are not connected ----------------
    public class GhostDevice { public string Id, Name, Class; }

    public static class Ghosts
    {
        const uint DIGCF_ALLCLASSES = 0x4, SPDRP_DEVICEDESC = 0x0, SPDRP_CLASS = 0x7, SPDRP_FRIENDLYNAME = 0xC, DIF_REMOVE = 0x5;
        const int CR_NO_SUCH_DEVINST = 0xD;
        // never offered: machine-level devices and software-created (ROOT\) entries that their programs recreate
        static readonly string[] KeepClasses = { "System", "Computer", "Processor", "LegacyDriver", "Firmware" };

        static void Walk(Func<IntPtr, SetupApi.SP_DEVINFO_DATA, string, bool> each)
        {
            IntPtr set = SetupApi.SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES);
            if (set == new IntPtr(-1)) throw new InvalidOperationException("SetupDiGetClassDevs failed (" + Marshal.GetLastWin32Error() + ")");
            try
            {
                var d = new SetupApi.SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SetupApi.SP_DEVINFO_DATA)) };
                bool stopped = false;
                for (uint i = 0; SetupApi.SetupDiEnumDeviceInfo(set, i, ref d); i++)
                {
                    var sb = new StringBuilder(512); int req;
                    string id = SetupApi.SetupDiGetDeviceInstanceIdW(set, ref d, sb, sb.Capacity, out req) ? sb.ToString() : "";
                    if (!each(set, d, id)) { stopped = true; break; }
                    d = new SetupApi.SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SetupApi.SP_DEVINFO_DATA)) };
                }
                // 3.27.1: a list that ended early must not look complete
                int last = Marshal.GetLastWin32Error();
                if (!stopped && last != 0 && last != SetupApi.ERROR_NO_MORE_ITEMS) throw new InvalidOperationException("SetupDiEnumDeviceInfo stopped early (" + last + ")");
            }
            finally { SetupApi.SetupDiDestroyDeviceInfoList(set); }
        }

        public static bool Offer(string id, string cls)
        {
            if (string.IsNullOrEmpty(id) || id.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase) || id.StartsWith(@"HTREE\", StringComparison.OrdinalIgnoreCase)) return false;
            return !KeepClasses.Any(k => string.Equals(k, cls, StringComparison.OrdinalIgnoreCase));
        }

        public static List<GhostDevice> List()
        {
            var l = new List<GhostDevice>();
            Walk((set, d, id) =>
            {
                uint st, pr;
                if (SetupApi.CM_Get_DevNode_Status(out st, out pr, d.DevInst, 0) != CR_NO_SUCH_DEVINST) return true;   // present
                var dd = d;
                string cls = SetupApi.Prop(set, ref dd, SPDRP_CLASS);
                if (!Offer(id, cls)) return true;
                string name = SetupApi.Prop(set, ref dd, SPDRP_FRIENDLYNAME); if (name.Length == 0) name = SetupApi.Prop(set, ref dd, SPDRP_DEVICEDESC);
                l.Add(new GhostDevice { Id = id, Name = name.Length > 0 ? name : id, Class = cls });
                return true;
            });
            return l.OrderBy(x => x.Class).ThenBy(x => x.Name).ToList();
        }

        // Removes only devices that are still not present. A removed device comes back when it is plugged in again.
        public static int Remove(IEnumerable<GhostDevice> devs) { int failed; return Remove(devs, out failed); }
        // failed = devices that were asked for but not removed (still connected, refused, or no longer listed)
        public static int Remove(IEnumerable<GhostDevice> devs, out int failed)
        {
            var want = new HashSet<string>(devs.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
            int asked = want.Count;
            int n = 0;
            Walk((set, d, id) =>
            {
                if (!want.Contains(id)) return true;
                uint st, pr;
                if (SetupApi.CM_Get_DevNode_Status(out st, out pr, d.DevInst, 0) != CR_NO_SUCH_DEVINST) { Log.W("Ghost device {0} is connected now: kept", id); return true; }
                var dd = d;
                if (SetupApi.SetupDiCallClassInstaller(DIF_REMOVE, set, ref dd)) { n++; Log.W("Removed ghost device: {0}", id); }
                else Log.W("Could not remove {0} ({1})", id, Marshal.GetLastWin32Error());
                return true;
            });
            failed = asked - n;
            return n;
        }
    }

}
