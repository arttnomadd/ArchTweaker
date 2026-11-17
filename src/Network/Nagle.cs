// ArchTweaker: Nagle algorithm (TcpAckFrequency, TCPNoDelay) on every adapter.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static class Nagle
    {
        const string IF = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
        static IEnumerable<string> ActiveIfs()
        {
            foreach (var s in Reg.SubKeys(Hive.HKLM, IF))
            {
                object ip = Reg.Get(Hive.HKLM, IF + "\\" + s, "DhcpIPAddress");
                object ip2 = Reg.Get(Hive.HKLM, IF + "\\" + s, "IPAddress");
                string a = ip != null ? ip.ToString() : (ip2 is string[] && ((string[])ip2).Length > 0 ? ((string[])ip2)[0] : "");
                if (!string.IsNullOrEmpty(a) && a != "0.0.0.0") yield return IF + "\\" + s;
            }
        }
        public static bool IsDisabled()
        {
            var l = ActiveIfs().ToList();
            return l.Count > 0 && l.All(p => Reg.Norm(Reg.Get(Hive.HKLM, p, "TcpAckFrequency")) == "1" && Reg.Norm(Reg.Get(Hive.HKLM, p, "TCPNoDelay")) == "1");
        }
        public static void Set(bool disableNagle)
        {
            foreach (var p in ActiveIfs())
            {
                if (disableNagle)
                {
                    Reg.Set(Hive.HKLM, p, "TcpAckFrequency", RegistryValueKind.DWord, 1);
                    Reg.Set(Hive.HKLM, p, "TCPNoDelay", RegistryValueKind.DWord, 1);
                }
                else
                {
                    Reg.DeleteValue(Hive.HKLM, p, "TcpAckFrequency");
                    Reg.DeleteValue(Hive.HKLM, p, "TCPNoDelay");
                }
            }
        }
    }
}
