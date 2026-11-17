// ArchTweaker: Store (UWP) apps: list and removal.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ---------------- APPS (UWP) ----------------
    public class AppPkg { public string Name, FullName, Version; public bool Bloat; public string Friendly; public string InstallLocation = ""; public System.Drawing.Bitmap Icon; }

    public static class AppsModule
    {
        // Matches the app list the original shipped icons for, plus newer inbox junk.
        public static readonly Dictionary<string, string> Bloat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Microsoft.BingWeather", "Weather" }, { "Microsoft.BingNews", "News" }, { "Microsoft.BingSearch", "Bing Search" },
            { "Microsoft.windowscommunicationsapps", "Mail & Calendar" }, { "Microsoft.WindowsFeedbackHub", "Feedback Hub" },
            { "Microsoft.GetHelp", "Get Help" }, { "Microsoft.Getstarted", "Tips" }, { "Microsoft.MixedReality.Portal", "Mixed Reality Portal" },
            { "Microsoft.MicrosoftOfficeHub", "Office / Microsoft 365 hub" }, { "Microsoft.Office.OneNote", "OneNote (Store)" },
            { "Microsoft.MSPaint", "Paint 3D" }, { "Microsoft.People", "People" }, { "Microsoft.WindowsSoundRecorder", "Sound Recorder" },
            { "Microsoft.MicrosoftSolitaireCollection", "Solitaire" }, { "Microsoft.MicrosoftStickyNotes", "Sticky Notes" },
            { "Microsoft.Microsoft3DViewer", "3D Viewer" }, { "Microsoft.WindowsAlarms", "Alarms & Clock" },
            { "Microsoft.WindowsCamera", "Camera" }, { "Microsoft.WindowsMaps", "Maps" }, { "Microsoft.ZuneMusic", "Groove / Media Player" },
            { "Microsoft.ZuneVideo", "Movies & TV" }, { "Microsoft.SkypeApp", "Skype" }, { "Microsoft.YourPhone", "Phone Link" },
            { "Microsoft.Todos", "To Do" }, { "Microsoft.PowerAutomateDesktop", "Power Automate" }, { "Clipchamp.Clipchamp", "Clipchamp" },
            { "MicrosoftTeams", "Teams (personal)" }, { "MSTeams", "Teams (new)" }, { "Microsoft.549981C3F5F10", "Cortana" },
            { "Microsoft.OutlookForWindows", "Outlook (new)" }, { "Microsoft.Windows.DevHome", "Dev Home" }, { "Microsoft.Copilot", "Copilot" },
            { "Microsoft.MicrosoftJournal", "Journal" }, { "MicrosoftCorporationII.QuickAssist", "Quick Assist" },
            { "Microsoft.GamingApp", "Xbox app (needed for Game Pass)" }, { "Microsoft.XboxGamingOverlay", "Xbox Game Bar" },
            { "Microsoft.Xbox.TCUI", "Xbox TCUI" }, { "Microsoft.XboxSpeechToTextOverlay", "Xbox speech overlay" },
            { "Microsoft.XboxIdentityProvider", "Xbox Identity (needed for Xbox sign-in)" }, { "Microsoft.WindowsStore", "Microsoft Store (keep unless sure!)" },
            { "Microsoft.Windows.Photos", "Photos" }, { "Microsoft.Paint", "Paint" }, { "Microsoft.ScreenSketch", "Snipping Tool" },
        };

        public static List<AppPkg> Load()
        {
            string o = Shell.PS("Get-AppxPackage -AllUsers | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable } | Sort-Object Name -Unique | ForEach-Object { $_.Name + '|' + $_.PackageFullName + '|' + $_.Version + '|' + $_.InstallLocation }");
            var list = new List<AppPkg>();
            foreach (var raw in o.Split('\n'))
            {
                var p = raw.Trim().Split('|');
                if (p.Length < 3) continue;
                string f;
                bool b = Bloat.TryGetValue(p[0], out f);
                list.Add(new AppPkg { Name = p[0], FullName = p[1], Version = p[2], Bloat = b, Friendly = b ? f : "", InstallLocation = p.Length > 3 ? p[3].Trim() : "" });
            }
            return list.OrderBy(a => a.Bloat ? 0 : 1).ThenBy(a => a.Name).ToList();
        }

        public static void Remove(IEnumerable<AppPkg> pkgs, bool deprovision)
        {
            foreach (var p in pkgs)
            {
                if (p.Name.IndexOf('\'') >= 0) continue;
                string s = "Get-AppxPackage -AllUsers -Name '" + p.Name + "' | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue;";
                if (deprovision) s += "Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq '" + p.Name + "' | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Out-Null;";
                string r = Shell.PS(s);
                Log.W("Removed {0} {1}", p.Name, r.Trim().Length > 0 ? "— " + r.Trim() : "");
            }
        }
    }
}
