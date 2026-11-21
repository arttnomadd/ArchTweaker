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
    public class AppPkg { public string Name, FullName, Version; public bool Bloat; public string Friendly; public string InstallLocation = ""; public System.Drawing.Bitmap Icon; public AppRisk Risk = AppRisk.Unknown; }

    // 3.38.4: what removing a Store app costs. Safe: nothing else needs it and the Store has it. Caution: a feature,
    // a game service or file type stops working. Keep: Windows or other programs need it. Unknown: not in our lists.
    public enum AppRisk { Safe, Caution, Keep, Unknown }

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

        // 3.38.4: apps whose removal breaks something people use. Each says what stops working.
        public static readonly Dictionary<string, string> CautionWhy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Microsoft.GamingApp", "Game Pass and Xbox PC games need it." },
            { "Microsoft.XboxGamingOverlay", "Game Bar: the Win+G overlay, FPS counter and game recording." },
            { "Microsoft.Xbox.TCUI", "Xbox sign-in and Game Pass screens in games use it." },
            { "Microsoft.XboxIdentityProvider", "Games that sign in with an Xbox account need it." },
            { "Microsoft.Windows.Photos", "The default picture viewer." },
            { "Microsoft.Paint", "Paint." },
            { "Microsoft.ScreenSketch", "Snipping Tool and the Win+Shift+S screenshot shortcut." },
            { "Microsoft.WindowsCamera", "On many laptops the only camera app." },
            { "Microsoft.ZuneMusic", "Media Player: the default music and video player." },
            { "Microsoft.WindowsNotepad", "Notepad, the default text editor." },
            { "Microsoft.WindowsCalculator", "Calculator." },
            { "Microsoft.WindowsTerminal", "The default terminal on Windows 11." },
            { "Microsoft.HEIFImageExtension", "Photos can't open HEIC pictures from phones without it." },
            { "Microsoft.WebpImageExtension", "Photos can't open WebP pictures without it." },
            { "Microsoft.RawImageExtension", "Photos can't open camera RAW files without it." },
            { "Microsoft.HEVCVideoExtension", "Videos in HEVC (H.265) don't play without it." },
            { "Microsoft.VP9VideoExtensions", "Videos in VP9 don't play without it." },
            { "Microsoft.AV1VideoExtension", "Videos in AV1 don't play without it." },
            { "RealtekSemiconductorCorp.RealtekAudioControl", "Realtek Audio Console: speaker, jack and microphone settings." },
            { "AppUp.IntelGraphicsExperience", "Intel Graphics Command Center: display and GPU settings." },
            { "AdvancedMicroDevicesInc-2.AMDRadeonSoftware", "AMD Software: driver and display settings." },
        };
        // 3.38.4: apps that Windows or other programs need. Removing them is rarely worth it.
        public static readonly Dictionary<string, string> KeepWhy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Microsoft.WindowsStore", "Reinstalling any Store app needs the Microsoft Store." },
            { "Microsoft.StorePurchaseApp", "The Microsoft Store needs it for purchases and licences." },
            { "Microsoft.DesktopAppInstaller", "App Installer: winget and installing .msix or .appx files need it." },
            { "Microsoft.Winget.Source", "winget reads its app list from it." },
            { "Microsoft.SecHealthUI", "The Windows Security app." },
            { "NVIDIACorp.NVIDIAControlPanel", "NVIDIA Control Panel: driver and display settings." },
        };
        // friendly names for known apps that are not in Bloat
        static readonly Dictionary<string, string> KnownNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Microsoft.WindowsNotepad", "Notepad" }, { "Microsoft.WindowsCalculator", "Calculator" }, { "Microsoft.WindowsTerminal", "Windows Terminal" },
            { "Microsoft.HEIFImageExtension", "HEIF image codec" }, { "Microsoft.WebpImageExtension", "WebP image codec" }, { "Microsoft.RawImageExtension", "Raw image codec" },
            { "Microsoft.HEVCVideoExtension", "HEVC video codec" }, { "Microsoft.VP9VideoExtensions", "VP9 video codec" }, { "Microsoft.AV1VideoExtension", "AV1 video codec" },
            { "RealtekSemiconductorCorp.RealtekAudioControl", "Realtek Audio Console" }, { "AppUp.IntelGraphicsExperience", "Intel Graphics Command Center" },
            { "AdvancedMicroDevicesInc-2.AMDRadeonSoftware", "AMD Software" }, { "Microsoft.StorePurchaseApp", "Store purchases" },
            { "Microsoft.DesktopAppInstaller", "App Installer (winget)" }, { "Microsoft.Winget.Source", "winget source" },
            { "Microsoft.SecHealthUI", "Windows Security" }, { "NVIDIACorp.NVIDIAControlPanel", "NVIDIA Control Panel" },
        };

        public static AppRisk RiskOf(string name)
        {
            name = name ?? "";
            if (KeepWhy.ContainsKey(name)) return AppRisk.Keep;
            if (CautionWhy.ContainsKey(name)) return AppRisk.Caution;
            if (Bloat.ContainsKey(name)) return AppRisk.Safe;
            return AppRisk.Unknown;
        }
        // the reason behind the badge, in English (the page translates it)
        public static string WhyOf(string name)
        {
            string w; name = name ?? "";
            if (KeepWhy.TryGetValue(name, out w) || CautionWhy.TryGetValue(name, out w)) return w;
            if (Bloat.ContainsKey(name)) return "Nothing else needs it. You can reinstall it from the Microsoft Store.";
            return "Not in ArchTweaker's lists. Check what it is before you remove it.";
        }
        public static string FriendlyOf(string name)
        {
            string f; name = name ?? "";
            if (Bloat.TryGetValue(name, out f) || KnownNames.TryGetValue(name, out f)) return f;
            return "";
        }

        public static List<AppPkg> Load()
        {
            string o = Shell.PS("Get-AppxPackage -AllUsers | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable } | Sort-Object Name -Unique | ForEach-Object { $_.Name + '|' + $_.PackageFullName + '|' + $_.Version + '|' + $_.InstallLocation }");
            var list = new List<AppPkg>();
            foreach (var raw in o.Split('\n'))
            {
                var p = raw.Trim().Split('|');
                if (p.Length < 3) continue;
                list.Add(new AppPkg { Name = p[0], FullName = p[1], Version = p[2], Bloat = Bloat.ContainsKey(p[0]), Friendly = FriendlyOf(p[0]), Risk = RiskOf(p[0]), InstallLocation = p.Length > 3 ? p[3].Trim() : "" });
            }
            // 3.38.4: Safe first, then Caution, Keep and the apps ArchTweaker doesn't know
            return list.OrderBy(a => (int)a.Risk).ThenBy(a => a.Friendly.Length > 0 ? a.Friendly : a.Name, StringComparer.OrdinalIgnoreCase).ToList();
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
