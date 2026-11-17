// ArchTweaker 1.8: tweaks ported from the FR33THY "Ultimate" guide (github.com/FR33THYFR33THY/Ultimate, MIT),
// re-implemented with ArchTweaker's exact-undo model. Only the clean, reversible ones are here; the
// destructive scripts (full services disable, Start/Search/Shell removal, safe-boot Defender disable) are left out.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static partial class Devices
    {
        public static IEnumerable<string> IntelGpuKeys()
        {
            return ClassKeys(GPU_CLASS).Where(k => (Reg.Get(Hive.HKLM, k, "ProviderName") ?? "").ToString().IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        public static bool HasIntelGpu() { return IntelGpuKeys().Any(); }
        public static IEnumerable<string> Intel3DKeys() { return IntelGpuKeys().Select(k => k + "\\3DKeys"); }
    }

    public static partial class Catalog
    {
        static readonly byte[] KbdLockMap =
        {
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x07,0x00,0x00,0x00,0x00,0x00,0x5b,0xe0,
            0x00,0x00,0x5c,0xe0,0x00,0x00,0x38,0x00,0x00,0x00,0x38,0xe0,0x00,0x00,0x01,0x00,
            0x01,0x00,0x0d,0x00,0x00,0x00,0x00,0x00
        };

        static void AddUltimate()
        {
            const Hive LM = Hive.HKLM, CU = Hive.HKCU;
            string GD = Cat.Gpu, IL = Cat.Input, RS = Cat.Responsiveness, X = DangerZone.CAT, P = Cat.Privacy;
            string game = PRESET_GAMING, safe = PRESET_SAFE, priv = PRESET_PRIVACY;

            // ---------------- GPU & DEVICES ----------------
            T("gd_flip_composed", GD, "Hardware composed: force immediate flip", "Forces the desktop compositor's flip chain into true immediate mode (ForceFlipTrueImmediateMode). On flip-model fullscreen games this can shave a little present latency. On some setups it does nothing or causes flicker, so test it.")
                .Info("From FR33THY's Ultimate guide. Undo deletes only this one value and keeps the rest of the GraphicsDrivers\\Scheduler key. The original script deletes the whole key.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Scheduler", "ForceFlipTrueImmediateMode", 1, null)
                .NeedsReboot().R(Risk.Moderate);

            T("gd_intel_latency", GD, "Intel iGPU: async flip + low-latency mode", "On Intel integrated graphics only: enables asynchronous flip and low-latency mode in the driver's 3DKeys. Has no effect on frames your discrete GPU renders — it only matters if a display is driven by the iGPU.")
                .Dev("Intel async flip", Devices.Intel3DKeys, "Global_AsyncFlipMode", RegistryValueKind.DWord, 2, false)
                .Dev("Intel low latency", Devices.Intel3DKeys, "Global_LowLatency", RegistryValueKind.DWord, 1, false)
                .Requires(Devices.HasIntelGpu, "Intel GPU").NeedsReboot().R(Risk.Moderate);

            // ---------------- INPUT & LATENCY ----------------
            T("il_keyboard_lockout", IL, "Lock out Windows & Alt keyboard shortcuts", "Stops the Windows key, Alt+Tab, Alt+Esc and the Win-key hotkeys, so you can't minimise a fullscreen game by accident. Also turns off the media-key service (hidserv) and remaps Esc to '='. Cut, copy and paste still work. Takes effect after a restart.")
                .Info("Reverting restores media keys (hidserv auto) and removes the key-disable policies and the scancode remap. From FR33THY's Ultimate guide.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\hidserv", "Start", 4, 3)
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoWinKeys", 1, null)
                .Dw(CU, ADV, "DisabledHotkeys", 1, null)
                .Bin(LM, @"SYSTEM\CurrentControlSet\Control\Keyboard Layout", "Scancode Map", KbdLockMap, null)
                .NeedsReboot().R(Risk.Advanced);

            // ---------------- RESPONSIVENESS ----------------
            T("sys_mmagent", RS, "Disable prefetch & app pre-launch (SuperFetch features)", "Turns off application-launch prefetching, app pre-launch and page combining, and stops the prefetcher writing layout files. On an NVMe SSD prefetch does almost nothing, so this mainly removes a little background CPU and disk work. (Memory compression has its own tweak.)")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", "EnablePrefetcher", 0, 3)
                .PS("Disable-MMAgent -ApplicationLaunchPrefetching -ErrorAction SilentlyContinue; Disable-MMAgent -ApplicationPreLaunch -ErrorAction SilentlyContinue; Disable-MMAgent -PageCombining -ErrorAction SilentlyContinue")
                .UndoPS("Enable-MMAgent -ApplicationLaunchPrefetching -ErrorAction SilentlyContinue; Enable-MMAgent -ApplicationPreLaunch -ErrorAction SilentlyContinue; Enable-MMAgent -PageCombining -ErrorAction SilentlyContinue")
                .R(Risk.Moderate);

            // ---------------- PRIVACY ----------------
            T("priv_backgroundapps", P, "Stop UWP apps running in the background", "Store/UWP apps can no longer run or update themselves in the background. Less background CPU, disk and RAM, and fewer surprise stutters. Doesn't affect normal desktop programs.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2, null)
                .Dw(CU, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "BackgroundAppGlobalToggle", 0, 1)
                .Dw(CU, @"SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1, null)
                .In(safe, priv, game);

            // ---------------- SECURITY (ADVANCED) ----------------
            T("sec_firewall", X, "Disable Windows Firewall", "Turns off the built-in firewall on the Domain, Private and Public profiles. Only sensible behind a router/NAT or a hardware firewall. Some anti-cheats and multiplayer titles expect the Windows firewall to be present, and malware has an easier time without it.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\DomainProfile", "EnableFirewall", 0, 1)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile", "EnableFirewall", 0, 1)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\PublicProfile", "EnableFirewall", 0, 1)
                .R(Risk.Advanced);

            T("sec_dep", X, "Disable Data Execution Prevention (DEP)", "Sets the boot flag nx = AlwaysOff. A few very old 32-bit games run slightly faster without DEP checks. Modern 64-bit games see no difference. It removes a real exploit mitigation. It only applies if Secure Boot is off in the BIOS.")
                .Info("Runs: bcdedit /set nx AlwaysOff   ·   undo: bcdedit /deletevalue nx")
                .DetectBy(() => (Probe.BcdValue("nx") ?? "").Trim().Equals("AlwaysOff", StringComparison.OrdinalIgnoreCase))
                .Cmd("bcdedit.exe", "/set nx AlwaysOff").UndoCmd("bcdedit.exe", "/deletevalue nx")
                .NeedsReboot().R(Risk.Advanced);

            T("sec_whql", X, "Allow unsigned drivers with Secure Boot (WHQL bypass)", "Lets drivers that aren't WHQL-signed load even with Secure Boot on. Useful for tools like old Profile Inspector builds or hobby drivers. It weakens driver-signing enforcement, so only enable it for a driver you trust.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\CI\Policy", "WHQLSettings", 1, null)
                .NeedsReboot().R(Risk.Advanced);

            T("sec_uac", X, "Disable User Account Control (UAC) entirely", "Sets EnableLUA = 0: no more elevation prompts at all. This is a big security reduction — every program runs with your full rights — and some Store/UWP apps refuse to run with UAC off. The 'UAC: don't dim the desktop' tweak is the milder option.")
                .Dw(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 0, 1)
                .NeedsReboot().R(Risk.Advanced);
        }

        static readonly Dictionary<string, object[]> GainsU = new Dictionary<string, object[]>
        {
            { "gd_flip_composed", new object[] { 1, "Flip-model fullscreen games: can shave roughly 1 frame of present latency on some setups, nothing on others. Measure with PresentMon. Can cause flicker — test." } },
            { "gd_intel_latency", new object[] { 1, "Intel integrated graphics only: lower flip latency on the iGPU. Zero effect when your discrete GPU drives the display." } },
            { "il_keyboard_lockout", new object[] { 0, "No speed effect. Prevents accidental Alt-Tab / Windows-key minimise during fullscreen play." } },
            { "sys_mmagent", new object[] { 1, "Stops prefetch/pre-launch background disk and CPU work and frees a little RAM. You notice it mostly on HDDs. Near zero on NVMe like yours." } },
            { "priv_backgroundapps", new object[] { 1, "Less background CPU, disk and RAM use from UWP apps. Fewer background-task stutters on busy systems. No average-FPS change." } },
            { "sec_firewall", new object[] { 0, "No measurable FPS gain (firewall filtering is negligible on modern CPUs). This is a convenience / anti-cheat-compat choice, and it lowers security." } },
            { "sec_dep", new object[] { 1, "Old 32-bit titles: tiny gains from skipping DEP checks. Modern 64-bit games: about 0. Lowers exploit protection; needs Secure Boot off." } },
            { "sec_whql", new object[] { 0, "No speed effect. Lets unsigned drivers load with Secure Boot on." } },
            { "sec_uac", new object[] { 0, "No speed effect. Removes all elevation prompts. Significant security reduction; some Store/UWP apps misbehave." } },
        };

        // Public, well-documented registry settings mined from a Reg Organizer tweak set — only the ones that are
        // genuinely new (not already in the 183), safe, reversible and still relevant on Windows 10/11.
        static void AddRegOrg()
        {
            const Hive LM = Hive.HKLM, CU = Hive.HKCU;
            string RS = Cat.Responsiveness, P = Cat.Privacy;
            string safe = PRESET_SAFE, game = PRESET_GAMING, priv = PRESET_PRIVACY;
            const string EXP_CU = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
            const string EXP_LM = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";

            T("ro_nolowdisk", RS, "Stop the \"low disk space\" pop-up", "Suppresses the balloon/notification Windows shows when a drive gets low on free space. Doesn't change any cleanup behaviour — it just stops the nag. Reverting deletes the value.")
                .Dw(CU, EXP_CU, "NoLowDiskSpaceChecks", 1, null)
                .In(safe).R(Risk.Safe);

            T("ro_norecentdocs", P, "Don't keep a recent-documents history", "Stops Windows from building the Recent Documents list (the per-user MRU of files you opened). Reduces a small privacy trail. Jump lists still show pinned items. Revert deletes the value.")
                .Dw(CU, EXP_CU, "NoRecentDocsMenu", 1, null)
                .In(priv).R(Risk.Safe);

            T("ro_shortcutresolve", RS, "Stop broken-shortcut network search", "When a shortcut's target has moved, Explorer normally scans drives and probes the network to \"resolve\" it, which can hang Explorer for seconds. This disables that link-tracking and resolve behaviour. Reverting deletes the three values.")
                .Info("Sets LinkResolveIgnoreLinkInfo, NoResolveSearch and NoResolveTrack. Shortcuts that still point at a valid target work as before.")
                .Dw(CU, EXP_CU, "LinkResolveIgnoreLinkInfo", 1, null)
                .Dw(CU, EXP_CU, "NoResolveSearch", 1, null)
                .Dw(CU, EXP_CU, "NoResolveTrack", 1, null)
                .In(safe, game).R(Risk.Safe);

            T("ro_openwithweb", P, "Remove web/Store lookup from \"Open with\"", "Removes the \"Look for an app in the Store\" and web-search entries from the Open With dialog. Then an unknown file type doesn't send you to the Store or a browser. Revert deletes the value.")
                .Dw(LM, EXP_LM, "NoInternetOpenWith", 1, null)
                .In(priv).R(Risk.Safe);

            T("ro_dnscache", RS, "Enlarge the client DNS cache", "Increases the size and lifetime limits of the local DNS resolver cache. Repeat lookups then come from memory for longer. A mild browsing-speed tweak with no effect on games. Revert deletes the four values (back to Windows defaults).")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "CacheHashTableBucketSize", 1, null)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "CacheHashTableSize", 384, null)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "MaxCacheEntryTtlLimit", 86400, null)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "MaxSOACacheEntryTtlLimit", 301, null)
                .NeedsReboot().R(Risk.Moderate);

            T("ro_regback", RS, "Re-enable automatic registry backups", "Since Windows 10 1803 the automatic registry-hive backup to %WinDir%\\System32\\config\\RegBack is disabled. This turns it back on, giving System Restore / recovery a recent copy of the hives. Costs a little disk and a bit of work at maintenance time. Reverting deletes the value.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Configuration Manager", "EnablePeriodicBackup", 1, null)
                .NeedsReboot().R(Risk.Safe);
        }

        static readonly Dictionary<string, object[]> GainsR = new Dictionary<string, object[]>
        {
            { "ro_nolowdisk", new object[] { 0, "No speed effect — removes the low-disk-space nag only." } },
            { "ro_norecentdocs", new object[] { 0, "No speed effect. Small privacy gain: no recent-files history kept." } },
            { "ro_shortcutresolve", new object[] { 1, "Removes multi-second Explorer hangs when a shortcut's target has moved. No FPS effect." } },
            { "ro_openwithweb", new object[] { 0, "No speed effect. Keeps unknown file types from sending you to the Store or web." } },
            { "ro_dnscache", new object[] { 1, "Slightly snappier repeat web lookups (served from the local cache for longer). No game-FPS effect." } },
            { "ro_regback", new object[] { 0, "No speed effect — a recovery/safety-net feature. Uses a little extra disk." } },
        };
    }
}
