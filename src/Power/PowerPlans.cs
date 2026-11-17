// ArchTweaker: Power plan helpers: import, duplicate and activate plans.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ================= POWER PLANS (tray) =================
    public static partial class PowerPlans
    {
        public class Plan { public string Guid, Name; public bool Active; }
        public static List<Plan> List()
        {
            var l = new List<Plan>();
            foreach (var line in Shell.Run("powercfg.exe", "/list", true).Split(new[] { '\n' }))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s+\((.+?)\)\s*(\*)?");
                if (m.Success) l.Add(new Plan { Guid = m.Groups[1].Value, Name = m.Groups[2].Value, Active = m.Groups[3].Success });
            }
            return l;
        }
        public static void Activate(string guid) { Shell.Run("powercfg.exe", "/setactive " + guid, true); PowerCache.Invalidate(); Log.W("Power plan activated: {0}", guid); }
    }
}
