// ArchTweaker: Tweaks from the cross-check of the synez packs and FR33THY's Ultimate guide (3.4).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static partial class Devices
    {
        public const string MEDIA_CLASS = "{4d36e96c-e325-11ce-bfc1-08002be10318}";
        public static IEnumerable<string> AudioKeys() { return ClassKeys(MEDIA_CLASS); }
        // 3.15: where PortCls keeps a device's idle timers
        public static IEnumerable<string> AudioPowerKeys() { return AudioKeys().Select(k => k + @"\PowerSettings"); }

        // Store apps registered for the current user, by package family prefix
        public static bool HasPackage(string prefix)
        {
            try
            {
                return Reg.SubKeys(Hive.HKCU, @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages")
                    .Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }
            catch { return true; }   // unknown: assume installed, so the protocol fix is not offered
        }
    }

    public static partial class Catalog
    {
        static readonly string[] OemAudioSvcs = { "NahimicService", "NahimicSvc32", "NahimicSvc64", "SS_CONEXANT_SERVICE", "SS_HDAudioService", "SoundResearch_Service", "DolbyDAX2API", "DolbyDAXAPI", "WavesSysSvc", "WavesAudioService", "AsusSSSvc", "RtkAudioUniversalService" };
        static readonly string[] KillerSvcs = { "KillerAnalyticsService", "KillerNetworkService", "KillerWirelessService", "KillerSmartphoneSignalService" };

        static string WinDisplayVersion()
        {
            var v = Reg.Get(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
            return v == null ? "" : v.ToString().Trim();
        }

        static void AddV34()
        {
            string P = Cat.Privacy, I = Cat.Interface, S = Cat.System, G = Cat.Gaming, PP = Cat.PowerPlan, IL = Cat.Input, GD = Cat.Gpu, N = Cat.Network, TS = Cat.TasksServices, C = Cat.Commands, X = DangerZone.CAT;
            string safe = PRESET_SAFE, game = PRESET_GAMING, priv = PRESET_PRIVACY;
            const string KERNEL = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
            const string TCPIP = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";

            // ---------------- INPUT & LATENCY ----------------
            T("il_timers", IL, "Kernel: spread timer expiry across cores", "DistributeTimers = 1: several cores handle timer interrupts, so they don't pile up on core 0. Most device interrupts also go to core 0. Can lower DPC spikes on core 0. Needs a restart.")
                .Info("Microsoft does not document it. The synez and FR33THY guides use it. Measure DPC latency (Latency page or LatencyMon) before and after.")
                .Dw(LM, KERNEL, "DistributeTimers", 1, null)
                .R(Risk.Moderate).NeedsReboot();
            T("gd_audio_power", GD, "Audio devices: no power saving", "Every sound device: Windows can't turn it off to save power (PnPCapabilities = 24). Fixes the click or pop when audio starts after silence, and USB DACs that drop out. ArchTweaker restores each original value when you untick.")
                .Dev("Audio PnPCapabilities", Devices.AudioKeys, "PnPCapabilities", RegistryValueKind.DWord, 24, false)
                .R(Risk.Moderate).NeedsReboot();

            // ---------------- GAMING ----------------
            T("mmcss_latency", G, "MMCSS: no lazy mode, games marked latency-sensitive", "The multimedia scheduler stays active instead of idling between bursts (NoLazyMode, AlwaysOn). It also marks the Games task as latency-sensitive. Pairs with 'MMCSS: prioritise games'.")
                .Info("From the synez roadmap. MMCSS reads these values on current Windows builds, but Microsoft does not document them. Undo deletes them.")
                .Dw(LM, MMSP, "NoLazyMode", 1, null)
                .Dw(LM, MMSP, "AlwaysOn", 1, null)
                .Sz(LM, MMSP + @"\Tasks\Games", "Latency Sensitive", "True", null)
                .R(Risk.Moderate).NeedsReboot();
            string sys32 = Environment.SystemDirectory;
            var links = T("gamebar_links", G, "Game Bar removed: stop the 'ms-gamebar' pop-up", "After you uninstall Xbox Game Bar, some games and Windows itself still open ms-gamebar links. Windows then asks you to find an app for them. This tweak points both link types at a harmless system program, so nothing pops up. ArchTweaker offers it only when Game Bar is not installed.")
                .Requires(() => !Devices.HasPackage("Microsoft.XboxGamingOverlay"), "Xbox Game Bar not installed");
            foreach (var proto in new[] { "ms-gamebar", "ms-gamingoverlay" })
            {
                string k = @"SOFTWARE\Classes\" + proto;
                links.Key(LM, k).Sz(LM, k, "", "URL:" + proto, null).Sz(LM, k, "URL Protocol", "", null).Sz(LM, k, "NoOpenWith", "", null)
                     .Key(LM, k + @"\shell\open\command").Sz(LM, k + @"\shell\open\command", "", "\"" + System.IO.Path.Combine(sys32, "systray.exe") + "\"", null);
            }

            // ---------------- INTERFACE ----------------
            T("ui_snap", I, "Snap: no layout pop-ups", "Windows 11 stops showing the snap-layout flyout when you hover over the maximise button. It also stops the bar that drops down from the top of the screen while you drag a window. Win+arrow snapping still works.")
                .Dw(CU, ADV, "EnableSnapAssistFlyout", 0, null)
                .Dw(CU, ADV, "EnableSnapBar", 0, null)
                .W11().NeedsRelogon();
            T("ui_startrec", I, "Start: hide the Recommended section", "Asks Windows to hide Start's Recommended list (policy HideRecommendedSection). Also turns off the tips and new-app suggestions in it. Some Home and Pro builds ignore the policy. Then the list stays but has no suggestions.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "HideRecommendedSection", 1, null)
                .Dw(LM, @"SOFTWARE\Microsoft\PolicyManager\current\device\Start", "HideRecommendedSection", 1, null)
                .Dw(CU, ADV, "Start_IrisRecommendations", 0, null)
                .W11().NeedsRelogon();
            T("ui_copilotbtn", I, "Taskbar: hide the Copilot button", "Removes the Copilot button from the taskbar on builds that still show it. The Copilot app itself stays.")
                .Dw(CU, ADV, "ShowCopilotButton", 0, null)
                .W11().NeedsRelogon();
            T("ui_dynlight", I, "Dynamic Lighting off", "Windows stops taking control of RGB keyboards, mice and lights. Your vendor's RGB app keeps control, and the two stop fighting over the colours.")
                .Dw(CU, @"Software\Microsoft\Lighting", "AmbientLightingEnabled", 0, null)
                .W11();
            T("ui_langhotkey", I, "No Alt+Shift / Ctrl+Shift layout switching", "Turns off the key combinations that switch keyboard language or layout, which fire by accident in games (Shift-walk plus Alt). Win+Space still switches. Takes effect after sign-out.")
                .Sz(CU, @"Keyboard Layout\Toggle", "Language Hotkey", "3", "1")
                .Sz(CU, @"Keyboard Layout\Toggle", "Hotkey", "3", "1")
                .Sz(CU, @"Keyboard Layout\Toggle", "Layout Hotkey", "3", "2")
                .NeedsRelogon();

            // ---------------- PRIVACY ----------------
            T("priv_cloudsearch", P, "Search: no cloud content from your accounts", "Windows Search stops showing results from your Microsoft account and work or school account (OneDrive, Outlook, SharePoint). It also stops keeping a search history on this device.")
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsMSACloudSearchEnabled", 0, null)
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsAADCloudSearchEnabled", 0, null)
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDeviceSearchHistoryEnabled", 0, null)
                .In(safe, priv);
            T("priv_paintai", P, "Paint: no AI features (Cocreator, Image Creator, generative fill)", "Turns off the cloud AI tools in Microsoft Paint by policy. Normal drawing and editing are unchanged.")
                .Dw(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint", "DisableCocreator", 1, null)
                .Dw(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint", "DisableImageCreator", 1, null)
                .Dw(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint", "DisableGenerativeFill", 1, null)
                .W11().In(priv);
            T("priv_lockscreen", P, "Lock screen: no Spotlight, tips or fun facts", "The lock screen keeps a fixed picture instead of downloading Spotlight images, and shows no tips, trivia or suggestions over it.")
                .Dw(CU, CDM, "RotatingLockScreenEnabled", 0, null)
                .Dw(CU, CDM, "RotatingLockScreenOverlayEnabled", 0, null)
                .Dw(CU, CDM, "SubscribedContent-338387Enabled", 0, null)
                .In(priv);
            T("priv_locktoasts", P, "No notifications on the lock screen", "Messages and reminders no longer appear while the PC is locked, so nobody walking past can read them.")
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK", 0, null)
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_ALLOW_CRITICAL_TOASTS_ABOVE_LOCK", 0, null)
                .In(safe, priv);
            T("priv_devmeta", P, "Don't download device info and icons", "Windows stops fetching manufacturer names, pictures and info for your devices from the internet. Drivers still install through Windows Update.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\Device Metadata", "PreventDeviceMetadataFromNetwork", 1, null)
                .In(safe, priv);
            T("priv_ink", P, "Turn off Windows Ink Workspace", "Removes the pen workspace (sticky notes and sketch shortcuts from the pen button). Pen input in apps still works.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\WindowsInkWorkspace", "AllowWindowsInkWorkspace", 0, null)
                .In(priv);

            // ---------------- SYSTEM ----------------
            T("sys_archiving", S, "Don't archive unused apps", "Windows 11 stops removing the files of Store apps you haven't opened for a while. Games and apps stay ready to start, offline too.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\Appx", "AllowAutomaticAppArchiving", 0, null)
                .W11();
            T("sys_storeauto", S, "Store: no automatic app updates", "Microsoft Store apps (and Store / Game Pass games) update only when you open the Store and choose to. Saves bandwidth during play. You have to update them yourself, including security fixes.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\WindowsStore", "AutoDownload", 2, null)
                .R(Risk.Moderate);
            string dv = WinDisplayVersion();
            T("sys_featurelock", S, "Stay on this Windows version", "Windows Update keeps installing monthly security and quality updates, but not the next feature update (the yearly new version). Untick to allow the upgrade again. Microsoft stops servicing a version after its end-of-support date.")
                .Info("Uses the documented TargetReleaseVersion policy with ProductVersion and TargetReleaseVersionInfo set to the version installed when ArchTweaker started" + (dv.Length > 0 ? " (" + dv + ")." : "."))
                .Requires(() => dv.Length > 0, "a Windows version number")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "TargetReleaseVersion", 1, null)
                .Sz(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ProductVersion", OSInfo.IsWin11 ? "Windows 11" : "Windows 10", null)
                .Sz(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "TargetReleaseVersionInfo", dv.Length > 0 ? dv : "0", null)
                .R(Risk.Moderate);

            // ---------------- POWER PLAN ----------------
            T("pp_deepsleep", PP, "No deep idle sleep (idle resiliency)", "Turns off the idle-resiliency deep sleep that Modern Standby PCs use while the screen is on and you're idle. Fewer wake-up delays after idle moments. Uses more power on laptops. Skipped where the setting doesn't exist.")
                .Pow("2e601130-5351-4d9d-8e04-252966bad054", "d502f7ee-1dc7-4efd-a55d-f04b6f5c0545", 0, "Deep sleep (idle resiliency)", "Disabled")
                .R(Risk.Moderate);

            // ---------------- NETWORK ----------------
            T("net_ports", N, "TCP: more ports, faster port reuse", "Raises the dynamic port limit to 65534 and lets closed connections release their port after 30 s instead of 120 s. Helps download managers, launchers and torrent clients that open many connections. No ping effect.")
                .Dw(LM, TCPIP, "MaxUserPort", 65534, null)
                .Dw(LM, TCPIP, "TcpTimedWaitDelay", 30, null)
                .NeedsReboot();
            T("cmd_rsc", C, "netsh: turn off receive segment coalescing (RSC)", "The network stack stops merging received packets into bigger ones. Each packet reaches the game as soon as it arrives, at the cost of a little more CPU on very fast downloads.")
                .Cmd("netsh.exe", "int tcp set global rsc=disabled").UndoCmd("netsh.exe", "int tcp set global rsc=enabled")
                .R(Risk.Moderate);
            T("cmd_timerclean", C, "bcdedit: remove old timer overrides", "Deletes useplatformclock (forced HPET), useplatformtick and tscsyncpolicy from the boot settings if an old guide set them, so Windows picks its own timer source again. Forcing HPET raises timer latency. Nothing to undo: these are Windows' defaults.")
                .Cmd("bcdedit.exe", "/deletevalue useplatformclock")
                .Cmd("bcdedit.exe", "/deletevalue useplatformtick")
                .Cmd("bcdedit.exe", "/deletevalue tscsyncpolicy")
                .NeedsReboot().In(game);

            // ---------------- TASKS & SERVICES ----------------
            var killer = T("svc_killer", TS, "Disable Killer Network services", "Killer (Intel / Rivet) network cards install services for traffic shaping, analytics and their control app. With them off the card works as a plain adapter, which is often lower and steadier ping. The Killer app stops working.")
                .Requires(() => KillerSvcs.Any(Svc.Exists), "a Killer network adapter")
                .R(Risk.Moderate);
            foreach (var k in KillerSvcs) killer.Service(k, 4, 2);

            // ---------------- DANGER ZONE ----------------
            T("dz_sac", X, "Turn off Smart App Control", "Smart App Control blocks apps that are unsigned or unknown to Microsoft. This also blocks many tweak tools, mods and older games. Turning it off is ONE-WAY on most builds: Windows only turns it on again after a reset or reinstall.")
                .Breaking("A Windows 11 protection that you can't turn on again on most builds. Unticking writes the evaluation value back, but Windows can keep it off until a reset.")
                .Requires(() => Reg.Get(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\CI\Policy", "VerifiedAndReputablePolicyState") != null, "Smart App Control (Windows 11 22H2+)")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\CI\Policy", "VerifiedAndReputablePolicyState", 0, 2)
                .W11().NeedsReboot();
            T("dz_bitlocker", X, "Turn off BitLocker / device encryption", "Decrypts the system drive in the background and stops Windows from encrypting it again automatically. Removes the small disk overhead of encryption. Unticking only allows encryption again. Turn it on again in Settings.")
                .Breaking("Protection of your files if you lose the PC or drive, or someone steals it: anyone can then read the disk. Work or school PCs can need BitLocker and lose access.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\BitLocker", "PreventDeviceEncryption", 1, null)
                .PS("manage-bde -off $env:SystemDrive")
                .NeedsReboot();
            // 3.30: CFG only, through the system mitigation bits; every other mitigation bit stays as it is
            T("dz_cfg", X, "Turn off Control Flow Guard (CFG) system-wide", "Sets the system-wide CFG mitigation to off. Programs built with CFG then skip the check on every indirect call. The gain is small and mostly in old games with many indirect calls. Other exploit protections stay on.")
                .Breaking("Exploit protection that stops many memory-corruption attacks from taking over a program. Some anti-cheats (FACEIT, Vanguard) can refuse to start with CFG off.")
                .Explains("Sets the CFG nibble (bits 40–43) of MitigationOptions under HKLM\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\kernel to 2 (always off). The other bits stay the same.   ·   undo: puts back the value the nibble had before")
                .DetectBy(CfgPolicy.IsOff)
                .OnApply(CfgPolicy.TurnOff).OnRevert(CfgPolicy.Restore)
                .NeedsReboot();
            var oem = T("dz_audiooem", X, "Disable OEM audio effect services", "Stops the Nahimic, Waves MaxxAudio, Dolby, Sonic Studio, Conexant / Sound Research and Realtek effect services that motherboard and laptop vendors install. They hook into every sound stream, and some (Nahimic especially) are known to cause stutter and crashes in games.")
                .Breaking("Virtual surround, equaliser and microphone noise cancelling from the vendor, and the vendor's sound app. On some laptops the speakers sound thinner without their tuning.")
                .Requires(() => OemAudioSvcs.Any(Svc.Exists), "an OEM audio service (Nahimic, Waves, Dolby…)")
                .NeedsReboot();
            foreach (var s in OemAudioSvcs) oem.Service(s, 4, 2);
        }

        static readonly Dictionary<string, object[]> GainsV34 = new Dictionary<string, object[]>
        {
            { "il_timers", new object[] { 1, "Can move timer DPCs off core 0: smaller DPC spikes where core 0 is busy with device interrupts. Often no measurable change." } },
            { "gd_audio_power", new object[] { 0, "Removes the pop or delay when audio starts after silence. No speed effect." } },
            { "mmcss_latency", new object[] { 1, "Slightly steadier scheduling for games and audio while MMCSS is active. Usually within noise." } },
            { "gamebar_links", new object[] { 0, "Removes a pop-up. No speed effect." } },
            { "ui_snap", new object[] { 0, "Fewer pop-ups while moving windows. No speed effect." } },
            { "ui_startrec", new object[] { 0, "A cleaner Start menu. No speed effect." } },
            { "ui_copilotbtn", new object[] { 0, "Tidiness only." } },
            { "ui_dynlight", new object[] { 0, "Stops RGB apps fighting with Windows. No speed effect." } },
            { "ui_langhotkey", new object[] { 0, "No accidental layout switches in games. No speed effect." } },
            { "priv_cloudsearch", new object[] { 0, "Privacy; local search results appear a little sooner." } },
            { "priv_paintai", new object[] { 0, "Privacy only." } },
            { "priv_lockscreen", new object[] { 0, "Privacy; no background image downloads." } },
            { "priv_locktoasts", new object[] { 0, "Privacy only." } },
            { "priv_devmeta", new object[] { 0, "Privacy; less background traffic when devices connect." } },
            { "priv_ink", new object[] { 0, "Tidiness only." } },
            { "sys_archiving", new object[] { 0, "Apps start right away instead of re-downloading first. No speed effect otherwise." } },
            { "sys_storeauto", new object[] { 1, "No Store downloads eating bandwidth or disk while you play. Nothing when nothing is updating." } },
            { "sys_featurelock", new object[] { 0, "No surprise feature upgrade (which resets many tweaks). No speed effect." } },
            { "pp_deepsleep", new object[] { 1, "Modern Standby PCs only: removes some wake-up delay after idle moments. Nothing on desktops without the setting." } },
            { "net_ports", new object[] { 0, "Helps only with very many connections at once. No ping effect." } },
            { "cmd_rsc", new object[] { 1, "Windows delivers packets one by one instead of in batches. This can trim tiny receive delays. Within noise on most connections." } },
            { "cmd_timerclean", new object[] { 2, "Only matters if an old guide forced a timer source. Forced HPET: removes ≈1–10 % FPS loss and higher QPC-call overhead. Frame pacing and DPC latency return to normal." } },
            { "svc_killer", new object[] { 2, "Killer cards: often a steadier ping with the shaping service off, and ≈30–80 MB RAM back. Nothing on other cards." } },
            { "dz_sac", new object[] { 0, "Lets blocked apps run. No speed effect." } },
            { "dz_bitlocker", new object[] { 1, "≈0–5 % disk throughput on drives without hardware encryption. Games: ≈0 %." } },
            { "dz_audiooem", new object[] { 2, "Systems with Nahimic or similar: removes a known cause of game stutter and crashes, and ≈50–150 MB RAM. Nothing where none is installed." } },
        };
    }
}
