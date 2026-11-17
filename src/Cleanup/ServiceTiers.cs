// ArchTweaker: Service helpers: start types and tiers.
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
    public static partial class Svc
    {
        // per-user service instances ("MessagingService_28d78"): the suffix differs per PC and per user
        // the Services key is read once per start: the catalog asks for every service of a tier
        static string[] names;
        public static IEnumerable<string> Instances(string template)
        {
            try
            {
                if (names == null) names = Reg.SubKeys(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services");
                return names.Where(n => n.StartsWith(template + "_", StringComparison.OrdinalIgnoreCase)).ToList();
            }
            catch { return new string[0]; }
        }
    }
}
