// ArchTweaker: NetBIOS over TCP/IP on every adapter.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static class NetBios
    {
        const string NB = @"SYSTEM\CurrentControlSet\Services\NetBT\Parameters\Interfaces";
        public static bool IsDisabled()
        {
            var k = Reg.SubKeys(Hive.HKLM, NB);
            return k.Length > 0 && k.All(i => Reg.Norm(Reg.Get(Hive.HKLM, NB + "\\" + i, "NetbiosOptions")) == "2");
        }
        public static void Set(bool disable)
        {
            foreach (var i in Reg.SubKeys(Hive.HKLM, NB))
                Reg.Set(Hive.HKLM, NB + "\\" + i, "NetbiosOptions", RegistryValueKind.DWord, disable ? 2 : 0);
        }
    }
}
