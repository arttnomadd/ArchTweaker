// ArchTweaker: The tweak catalog: categories, the builder (T, Dw, Sz, …) and the core tweak list.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static partial class Catalog
    {
        const Hive LM = Hive.HKLM, CU = Hive.HKCU;
        public const string PRESET_SAFE = "Recommended", PRESET_GAMING = Cat.Gaming, PRESET_PRIVACY = "Max Privacy";
        public static readonly string[] Categories = { Cat.Privacy, Cat.Interface, Cat.Explorer, Cat.System, Cat.Responsiveness, Cat.Gaming, Cat.PowerPlan, Cat.Input, Cat.Gpu, Cat.Network, HARDENING, Cat.TasksServices, Cat.Commands, DangerZone.CAT };

        // 3.13: Build() fills `all` while it runs (T() appends to it). Other threads wait for the finished list:
        // before, a background count could see a half-built catalog or build it twice (sidebar showed 0/31, 0/65).
        static List<Tweak> all;
        static volatile List<Tweak> done;
        static readonly object buildGate = new object();
        public static List<Tweak> All
        {
            get
            {
                var d = done; if (d != null) return d;
                lock (buildGate)
                {
                    if (done != null) return done;
                    if (all != null) return all;   // a call from inside Build() on this thread: the list so far, as before
                    done = Build();
                    return done;
                }
            }
        }

        // 3.30: a Chromium browser installed for all users or for this user (path under Program Files / Local AppData)
        static bool ChromiumInstalled(string rel)
        {
            foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
                if (!string.IsNullOrEmpty(root) && System.IO.File.Exists(System.IO.Path.Combine(root, rel))) return true;
            return false;
        }

        internal static Tweak T(string id, string cat, string title, string desc)
        {
            var t = new Tweak { Id = id, Category = cat, Title = title, Desc = desc };
            all.Add(t);
            return t;
        }

        const string ADV = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string CDM = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        const string MMSP = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        const string BLOCKED = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        const string GCS = @"System\GameConfigStore";

        static List<Tweak> Build()
        {
            StartupTrace.Mark("tweak catalog: building");
            all = new List<Tweak>();
            string S = Cat.System, X = DangerZone.CAT;   // 3.32: the other category names are in the CoreTweaks.* parts

            AddCoreDesktop();   // 3.32: CoreTweaks.Desktop.cs
            AddCoreSystem();    // 3.32: CoreTweaks.System.cs

            AddMore();
            AddV15();
            int ultFirst = all.Count;
            AddUltimate();
            foreach (var ut in all.Skip(ultFirst)) ut.Origin = "FR33THY Ultimate";
            int regFirst = all.Count;
            AddRegOrg();
            foreach (var rt in all.Skip(regFirst)) rt.Origin = "Reg Organizer set";
            AddDisplay();

            AddV34();
            AddV35();
            AddV36();
            AddV39();
            AddV315();
            AddV321();
            AddV324();   // 3.24: audio

            // ---------------- DANGER ZONE (3.4; was "Security (advanced)") ----------------
            T("uacdim", S, "UAC: don't dim the desktop", "UAC still asks, but without the secure-desktop dimming (faster prompts). Slightly lowers protection.")
                .Dw(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "PromptOnSecureDesktop", 0, 1).R(Risk.Moderate);
            T("smartscreen", X, "Disable SmartScreen for apps & files", "No more 'Windows protected your PC' for unknown downloads. Removes a real safety net.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", 0, null)
                .Sz(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled", "Off", "Warn")
                .R(Risk.Advanced);
            T("defender", X, "Disable Defender real-time protection (policy)", "Only takes effect if Tamper Protection is OFF in Windows Security first. Use it only if you run another antivirus.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring", 1, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection", "DisableBehaviorMonitoring", 1, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", "SpynetReporting", 0, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", "SubmitSamplesConsent", 2, null)
                .R(Risk.Advanced).NeedsReboot();
            T("vbs", X, "Disable Memory Integrity / VBS", "Can add a few % FPS on some CPUs, but turns off kernel exploit protection. Some anti-cheats (for example Valorant or FACEIT on Windows 11) can need it on.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0, 1)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", 0, null)
                .R(Risk.Advanced).NeedsReboot();

            PostBuild();            // 3.7: every post-build pass, in order (V37.cs)
            StartupTrace.Mark("tweak catalog: " + all.Count + " tweaks");
            Overlay.ApplyTo(all);   // optional downloaded additions/patches (registry values only)
            return all;
        }
    }
}
