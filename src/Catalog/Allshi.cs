// ArchTweaker: Tweaks from the ALLSHI stash cross-check (3.6).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static partial class Catalog
    {
        public const string HARDENING = Cat.Hardening;
        const string GD_KEY = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
        // existing tweaks that protect rather than speed up move to Hardening (ids stay the same)
        internal static readonly string[] ToHardening = { "llmnr", "smb1", "netbios", "mdnsoff", "net_remoteassist", "sys_wpbt" };
        static readonly string[] Autologgers = { "NtfsLog", "WiFiDriverIHVSession", "WiFiDriverSession", "WiFiSession", "SleepStudyTraceSession", "1DSListener", "DiagLog", "LwtNetLog",
            "Microsoft-Windows-Rdp-Graphics-RdpIdd-Trace", "NetCore", "RadioMgr", "ReFSLog", "WdiContextLog", "Circular Kernel Context Logger" };
        const string AUTOLOGGER = @"SYSTEM\CurrentControlSet\Control\WMI\Autologger";
        const string SCHANNEL = @"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols";

        static Tweak Keep(Tweak t, string path, string name, object value, RegistryValueKind kind = RegistryValueKind.DWord)
        {
            t.Ops.Add(new KeepOp { Path = path, Name = name, Value = value, Kind = kind });
            return t;
        }
        static Tweak Disable(Tweak t, string label, string[] ids, string[] names)
        {
            var op = new DevDisableOp { Label = label, Ids = ids, Names = names };
            t.Ops.Add(op);
            t.Requires(op.Present, "the device on this PC");
            return t;
        }

        static void AddV36()
        {
            string P = Cat.Privacy, GD = Cat.Gpu, H = HARDENING, X = DangerZone.CAT, PR = Cat.PowerPlan, IL = Cat.Input;
            string game = PRESET_GAMING, priv = PRESET_PRIVACY, safe = PRESET_SAFE;

            // ================= TIER 1 =================
            var al = T("priv_autologgers", P, "Turn off background trace sessions (autologgers)", "Windows starts about 15 event-trace sessions at boot that log NTFS, Wi-Fi, sleep study, network, ReFS and context data to disk all the time. This stops them from starting. Less background disk and CPU work, and less data kept about you.")
                .TurnsOff("NtfsLog, WiFi sessions, SleepStudy, 1DSListener, DiagLog, LwtNetLog, RDP graphics trace, NetCore, RadioMgr, ReFSLog, WdiContextLog and the Circular Kernel Context Logger.")
                .Breaking("Diagnostic data Windows and support use: the sleep study report (powercfg /sleepstudy), Wi-Fi and network diagnostics, and some troubleshooters.")
                .Requires(() => Autologgers.Any(a => Reg.KeyExists(Hive.HKLM, AUTOLOGGER + "\\" + a)), "Windows autologger sessions")
                .NeedsReboot().In(priv);
            foreach (var a in Autologgers) if (Reg.KeyExists(Hive.HKLM, AUTOLOGGER + "\\" + a)) al.Dw(LM, AUTOLOGGER + "\\" + a, "Start", 0, 1);

            T("gd_usb_stack", GD, "USB hubs, HID and USB audio drivers: no power saving", "Driver-wide settings from synez / mmm pain. The Windows USB driver never selectively suspends a port (DisableSelectiveSuspend). HID devices (mice, keyboards, controllers) don't suspend or idle, and USB 3 hubs don't suspend. The xHCI controller doesn't use enhanced power management, and USB audio doesn't power down. Adds to the power-plan USB setting, which some drivers ignore.")
                .TurnsOff("USB selective suspend (driver-wide), HID suspend and idle, USB3 hub suspend, xHCI enhanced power management, USB audio power saving and HID wake-up.")
                .Breaking("Laptop battery life, and waking the PC with the keyboard or mouse from sleep.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\USB", "DisableSelectiveSuspend", 1, null)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\HidUsb\Parameters", "SelectiveSuspendEnabled", 0, null)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\HidUsb\Parameters", "DeviceIdleEnabled", 0, null)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\HidUsb\Parameters", "DisableWakeFromSuspend", 1, null)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\USBHUB3\Parameters", "SelectiveSuspendEnabled", 0, null)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\USBXHCI\Parameters", "EnhancedPowerManagementEnabled", 0, null)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\usbaudio\Parameters", "PowerSettings", 0, null)
                .NeedsReboot().In(game);

            Disable(T("gd_dev_virtual", GD, "Disable unused virtual system devices", "Disables the NDIS Virtual Network Adapter Enumerator, the Remote Desktop Device Redirector bus, the Microsoft Kernel Debug Network Adapter and the PC speaker. They load drivers and handle interrupts for features a gaming PC doesn't use.")
                .TurnsOff("NDIS Virtual Network Adapter Enumerator, Remote Desktop Device Redirector, Kernel Debug Network Adapter, System speaker.")
                .Breaking("Printer, drive and audio redirection in Remote Desktop sessions to this PC. Also kernel debugging over the network, some VPNs and virtual switches that use the NDIS virtual bus, and BIOS-style beeps.")
                .R(Risk.Moderate).NeedsReboot(), "virtual system devices", new[] { @"ROOT\NDISVIRTUALBUS\", @"ROOT\RDPBUS\", @"ROOT\KDNIC\", @"ACPI\PNP0800\" }, null);
            Disable(T("gd_dev_nvaudio", GD, "Disable NVIDIA Virtual Audio Device", "The NVIDIA Virtual Audio Device (Wave Extensible) is only used to stream game audio to an NVIDIA Shield or GameStream client. Disabling it removes an audio endpoint games and Discord can pick by mistake.")
                .TurnsOff("NVIDIA Virtual Audio Device.")
                .Breaking("Audio when streaming games to an NVIDIA Shield or a GameStream / Moonlight client.")
                .R(Risk.Moderate), "NVIDIA virtual audio", null, new[] { "NVIDIA Virtual Audio Device" });

            T("gd_laptop_display", GD, "Laptop panel: no self-refresh, dynamic refresh or display power saving", "Laptop screens drop to a lower refresh rate or freeze frames (panel self-refresh) to save power, which can cause flicker, stutter and uneven frame pacing. This keeps the panel at its full refresh rate. Laptops only.")
                .TurnsOff("Panel self-refresh (PSR), dynamic / adaptive refresh rate and display power-saving mode.")
                .Breaking("Battery life: about 5–15 % less runtime, as the panel refreshes at full rate all the time.")
                .Requires(() => Laptop.Is, "a laptop (battery)")
                .Dw(LM, GD_KEY, "PanelSelfRefresh", 0, null)
                .Dw(LM, GD_KEY, "PowerSavingModeEnabled", 0, null)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\Power\ModernSleep", "AdaptiveRefreshRate", 0, null)
                .R(Risk.Moderate).NeedsReboot();

            T("gd_nv_dlss_latest", GD, "NVIDIA: always use the latest DLSS model", "Turns on the driver's DLSS Super Resolution override and sets it to 'Latest'. Then every DLSS game uses the newest DLSS (transformer) model, not the one it shipped with. Sharper image, but slightly less FPS gain than old DLSS.")
                .TurnsOff("Each game's own DLSS preset.")
                .Breaking("A few games with anti-cheat or old DLSS versions can crash or look wrong: untick it or use per-game overrides in the NVIDIA App.")
                .NvZ(0x10E41E01, 1, "DLSS-SR override", "Enabled")
                .NvZ(0x10E41DF3, 0xFFFFFF, "DLSS-SR preset", "Latest")
                .Requires(() => NvDrs.Available, "NVIDIA driver").R(Risk.Moderate);
            T("gd_nv_dlss_indicator", GD, "NVIDIA: show the DLSS indicator in games", "Shows a small text overlay in the corner of DLSS games with the DLSS version and preset in use. Use it to check that the override works. Turn it off when you're done.")
                .Dw(LM, @"SOFTWARE\NVIDIA Corporation\Global\NGXCore", "ShowDlssIndicator", 1024, null)
                .Requires(Devices.HasNvidia, "NVIDIA GPU");

            // ================= TIER 2: HARDENING =================
            var ntlm = T("hd_ntlm", H, "NTLMv2 only (no LM / NTLMv1)", "Windows only sends NTLMv2 responses and refuses LM and NTLMv1, which are easy to crack if someone captures them on the network.")
                .TurnsOff("LM and NTLMv1 authentication.")
                .Breaking("Very old NAS boxes, Samba servers and printers' scan-to-folder that only speak NTLMv1 (mostly before 2010).")
                .R(Risk.Moderate);
            Keep(ntlm, @"SYSTEM\CurrentControlSet\Control\Lsa", "LmCompatibilityLevel", 5);
            Keep(ntlm, @"SYSTEM\CurrentControlSet\Control\Lsa", "NoLMHash", 1);
            var tls = T("hd_tls", H, "TLS 1.2+ only (SSL 2/3, TLS 1.0/1.1 off)", "Windows' own TLS (for .NET apps, Windows Update, Outlook and many launchers) refuses the old SSL 2.0/3.0 and TLS 1.0/1.1 protocols. These protocols have known attacks. ArchTweaker turns on TLS 1.2 explicitly. TLS 1.3 stays as Windows sets it.")
                .TurnsOff("SSL 2.0, SSL 3.0, TLS 1.0 and TLS 1.1 in Windows (Schannel).")
                .Breaking("Old router admin pages, old game launchers and business apps that only speak TLS 1.0 / 1.1. Chrome and Firefox use their own TLS, so this does not affect them.")
                .R(Risk.Moderate).NeedsReboot();
            foreach (var proto in new[] { "SSL 2.0", "SSL 3.0", "TLS 1.0", "TLS 1.1" })
                foreach (var side in new[] { "Client", "Server" })
                { Keep(tls, SCHANNEL + "\\" + proto + "\\" + side, "Enabled", 0); Keep(tls, SCHANNEL + "\\" + proto + "\\" + side, "DisabledByDefault", 1); }
            foreach (var side in new[] { "Client", "Server" })
            { Keep(tls, SCHANNEL + @"\TLS 1.2\" + side, "Enabled", 1); Keep(tls, SCHANNEL + @"\TLS 1.2\" + side, "DisabledByDefault", 0); }
            var dns = T("hd_dnsclient", H, "DNS: no smart multi-adapter resolution, no parallel A/AAAA", "Windows stops sending each DNS lookup over every network adapter at once, which leaks lookups past a VPN. It also asks for IPv4 and IPv6 addresses one after the other.")
                .TurnsOff("Smart multi-homed name resolution and parallel A / AAAA queries.")
                .Breaking("A first lookup can be a few ms slower when the primary DNS server is slow.")
                .In(safe, priv);
            Keep(dns, @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "DisableSmartNameResolution", 1);
            Keep(dns, @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "DisableParallelAandAAAA", 1);
            Keep(T("hd_icmpredirect", H, "Ignore ICMP redirects", "Windows ignores ICMP redirect packets, which an attacker on the same network can use to reroute your traffic through their machine.")
                .TurnsOff("Acting on ICMP redirects.").In(safe), @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "EnableICMPRedirect", 0);
            var rdp = T("hd_rdp", H, "Block Remote Desktop connections to this PC", "Nobody can sign in to this PC with Remote Desktop. Home editions can't accept RDP anyway. On Pro this stops attacks on port 3389.")
                .TurnsOff("Incoming Remote Desktop connections.")
                .Breaking("Connecting to this PC with Remote Desktop (connecting out to other PCs still works).");
            Keep(rdp, @"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", 1);
            T("hd_autorun", H, "No AutoRun / AutoPlay for any drive", "USB sticks, discs and network drives never start anything on their own when connected, which blocks a classic way malware spreads.")
                .TurnsOff("AutoRun and AutoPlay for every drive type.")
                .Breaking("The AutoPlay pop-up when you plug in a phone, camera or USB stick.")
                .Dw(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDriveTypeAutoRun", 255, null)
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDriveTypeAutoRun", 255, null)
                .In(safe);
            Keep(T("hd_lmhosts", H, "Don't use the LMHOSTS file", "Windows stops looking up names in the legacy LMHOSTS file, an old name-spoofing route.")
                .TurnsOff("LMHOSTS lookups.").Breaking("Very old networks that rely on an LMHOSTS file (almost none today)."), @"SYSTEM\CurrentControlSet\Services\NetBT\Parameters", "EnableLMHOSTS", 0);
            var ntlmOut = T("dz_ntlm_out", X, "Block all outgoing NTLM", "This PC refuses to sign in to other machines with NTLM. Only Kerberos (domain networks) is left. Stops NTLM relay and hash-capture attacks completely.")
                .Breaking("Signing in to NAS drives, other PCs' shared folders and network printers on home networks: they all use NTLM without a domain.");
            Keep(ntlmOut, @"SYSTEM\CurrentControlSet\Control\Lsa\MSV1_0", "RestrictSendingNTLMTraffic", 2);

            // ================= TIER 3: EXPERIMENTAL =================
            T("exp_serialtimer", IL, "Kernel: serialize timer expiration", "SerializeTimerExpiration = 1 from the mmm pain pack: Windows handles timer expirations one after another, not in a batch. Some users report smoother frame times on Windows 11 24H2+ systems. Not documented.")
                .Info("Experimental. Compare with the A/B test page (frame times and DPC latency) before keeping it.")
                .Breaking("Undocumented: on some systems it raises DPC latency or makes timing worse.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "SerializeTimerExpiration", 1, null)
                .R(Risk.Moderate).NeedsReboot().Exp();
            var lt = T("exp_latencytol", PR, "Power: lowest latency tolerance for devices and graphics", "Sets the latency-tolerance hints that Windows gives devices and the graphics power manager to 1 (the lowest). Then they avoid deep idle states that are slow to wake. From the mmm pain pack ('don't tolerate high DPC/ISR').")
                .Info("Experimental. Measure DPC latency and frame times on the A/B test page.")
                .TurnsOff("Deep idle states of devices and the GPU that take long to wake.")
                .Breaking("Idle power and laptop battery life. On a few systems, no change at all.")
                .R(Risk.Moderate).NeedsReboot().Exp();
            foreach (var n in new[] { "ExitLatency", "ExitLatencyCheckEnabled", "Latency", "LatencyToleranceDefault", "LatencyToleranceFSVP", "LatencyTolerancePerfOverride", "LatencyToleranceScreenOffIR", "LatencyToleranceVSyncEnabled", "RtlCapabilityCheckLatency" })
                lt.Dw(LM, @"SYSTEM\CurrentControlSet\Control\Power", n, 1, null);
            foreach (var n in new[] { "DefaultD3TransitionLatencyActivelyUsed", "DefaultD3TransitionLatencyIdleLongTime", "DefaultD3TransitionLatencyIdleMonitorOff", "DefaultD3TransitionLatencyIdleNoContext", "DefaultD3TransitionLatencyIdleShortTime", "DefaultD3TransitionLatencyIdleVeryLongTime",
                "DefaultLatencyToleranceIdle0", "DefaultLatencyToleranceIdle0MonitorOff", "DefaultLatencyToleranceIdle1", "DefaultLatencyToleranceIdle1MonitorOff", "DefaultLatencyToleranceMemory", "DefaultLatencyToleranceNoContext", "DefaultLatencyToleranceNoContextMonitorOff",
                "DefaultLatencyToleranceOther", "DefaultLatencyToleranceTimerPeriod", "DefaultMemoryRefreshLatencyToleranceActivelyUsed", "DefaultMemoryRefreshLatencyToleranceMonitorOff", "DefaultMemoryRefreshLatencyToleranceNoContext",
                "Latency", "MaxIAverageGraphicsLatencyInOneBucket", "MiracastPerfTrackGraphicsLatency", "MonitorLatencyTolerance", "MonitorRefreshLatencyTolerance", "TransitionLatency" })
                lt.Dw(LM, GD_KEY + @"\Power", n, 1, null);
            T("dz_nv_rmpower", X, "NVIDIA driver: GPU PCIe power saving off, per-core DPCs", "Driver-key settings from the mmm pain pack. RMDisableGpuASPMFlags = 3 keeps the GPU's PCIe link out of ASPM power states. RmGpsPsEnablePerCpuCoreDpc = 1 spreads the driver's DPCs across CPU cores.")
                .Info("Experimental. Undocumented NVIDIA driver keys: measure DPC latency before and after.")
                .Breaking("Idle power and heat. On some driver versions: instability or a black screen at boot (undo from Safe Mode).")
                .Dev("NVIDIA RMDisableGpuASPMFlags", Devices.NvidiaKeys, "RMDisableGpuASPMFlags", RegistryValueKind.DWord, 3, false)
                .Dev("NVIDIA RmGpsPsEnablePerCpuCoreDpc", Devices.NvidiaKeys, "RmGpsPsEnablePerCpuCoreDpc", RegistryValueKind.DWord, 1, false)
                .Requires(Devices.HasNvidia, "NVIDIA GPU").NeedsReboot().Exp();
            T("gd_amd_lowlat", GD, "AMD driver: Anti-Lag on, no clock deep sleep, no memory stutter", "Driver-key settings from the mmm pain pack. KMD_DeLagEnabled = 1 forces Radeon Anti-Lag on in every game. PP_SclkDeepSleepDisable = 1 stops the GPU core from deep-sleeping between frames. StutterMode = 0 turns off VRAM self-refresh at idle.")
                .Info("Experimental. Compare frame times on the A/B test page. Adrenalin can override these after an update.")
                .TurnsOff("GPU core deep sleep and VRAM stutter (self-refresh) power saving.")
                .Breaking("Idle power +10–30 W and more heat. Anti-Lag in every game can conflict with in-game Anti-Lag 2 or with some anti-cheats.")
                .Dev("AMD KMD_DeLagEnabled", Devices.AmdGpuKeys, "KMD_DeLagEnabled", RegistryValueKind.DWord, 1, false)
                .Dev("AMD PP_SclkDeepSleepDisable", Devices.AmdGpuKeys, "PP_SclkDeepSleepDisable", RegistryValueKind.DWord, 1, false)
                .Dev("AMD StutterMode", Devices.AmdGpuKeys, "StutterMode", RegistryValueKind.DWord, 0, false)
                .Requires(Devices.HasAmdGpu, "AMD GPU").R(Risk.Moderate).NeedsReboot().Exp();
            Disable(T("dz_dev_hpet", X, "Disable the High Precision Event Timer device", "Disables HPET in Device Manager so Windows uses the CPU's TSC timer. Some guides recommend it for lower timer overhead. On most modern systems Windows already prefers TSC, so the effect is small or none.")
                .Info("Experimental. Contested: measure frame times before and after.")
                .Breaking("Some boards and older software need HPET: clock drift, audio crackle, or games and timers running at the wrong speed.")
                .NeedsReboot().Exp(), "HPET", new[] { @"ACPI\PNP0103\" }, null);
        }

        // ================= 3.6.2 =================
        // tweaks with nothing to undo say so on the row and in the details panel
        static void UndoNotes()
        {
            foreach (var id in new[] { "cmd_trim", "cmd_autotuning", "cmd_rss", "cmd_timerclean" })
            { var t = all.FirstOrDefault(x => x.Id == id); if (t != null) t.UndoNote = "defaults"; }
            foreach (var t in all.Where(x => x.UndoNote == null && x.Ops.Count == 0 && x.RevertCmds.Count == 0 && x.RevertExtra == null && (x.ApplyCmds.Count > 0 || x.ApplyExtra != null)))
                t.UndoNote = "permanent";
            var d3 = all.FirstOrDefault(x => x.Id == "3dobjects");   // the 3D Objects entry is an empty key: re-create it when there is no backup
            if (d3 != null) foreach (var k in d3.Ops.OfType<KeyDelOp>()) k.Recreate = true;
        }

        // ================= 3.6.1: duplicates removed, contradicting pairs =================
        // merged tweaks: an old id in a saved profile is read as the tweak that took it over
        public static readonly Dictionary<string, string> Renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "bgapps", "priv_backgroundapps" },          // same two values; priv_backgroundapps also sets the Search toggle
            { "gd_usb_suspend", "gd_usb_stack" },          // DisableSelectiveSuspend is now part of the USB driver tweak
            { "cmd_platformclock", "cmd_timerclean" },     // useplatformclock is now removed with the other timer overrides
        };
        public static string CurrentId(string id) { string to; return id != null && Renamed.TryGetValue(id, out to) ? to : id; }

        // pairs that undo each other's intent: switching one on switches the other off
        internal static readonly string[][] ExcludePairs =
        {
            new[] { "il_timers", "exp_serialtimer" },        // spread timer expiry across cores vs serialize it
            new[] { "gd_nv_drs_frl", "gd_nv_drs_vsync" },    // frame cap for G-SYNC vs G-SYNC forced off
            new[] { "dnscloudflare", "dnsadguard" },         // 3.39: one set of DNS servers at a time
        };
        static void Exclusions()
        {
            foreach (var p in ExcludePairs)
            {
                var a = all.FirstOrDefault(t => t.Id == p[0]); var b = all.FirstOrDefault(t => t.Id == p[1]);
                if (a == null || b == null) continue;
                a.Excludes = a.Excludes.Concat(new[] { b.Id }).Distinct().ToArray();
                b.Excludes = b.Excludes.Concat(new[] { a.Id }).Distinct().ToArray();
            }
        }

        // what a tweak writes, one key per target: used to prove no two tweaks change the same thing
        public static IEnumerable<string> Targets(Tweak t)
        {
            foreach (var o in t.Ops)
            {
                var vo = o as VOp; if (vo != null) { yield return ("reg|" + vo.H + "\\" + vo.Path + "|" + vo.Name).ToLowerInvariant(); continue; }
                var ko = o as KeepOp; if (ko != null) { yield return ("reg|" + ko.H + "\\" + ko.Path + "|" + ko.Name).ToLowerInvariant(); continue; }
                var ka = o as KeyAddOp; if (ka != null) { yield return ("key|" + ka.H + "\\" + ka.Path).ToLowerInvariant(); continue; }
                var kd = o as KeyDelOp; if (kd != null) { yield return ("key|" + kd.H + "\\" + kd.Path).ToLowerInvariant(); continue; }
                var so = o as SvcOp; if (so != null) { yield return ("svc|" + so.Name).ToLowerInvariant(); continue; }
                var sp = o as SvcPrevOp; if (sp != null) { yield return ("svc|" + sp.Name).ToLowerInvariant(); continue; }
                var ta = o as TaskOp; if (ta != null) { yield return ("task|" + ta.Path).ToLowerInvariant(); continue; }
                var po = o as PowerOp; if (po != null) { yield return ("pow|" + po.Sub + "|" + po.Setting).ToLowerInvariant(); continue; }
                var nv = o as NvDrsOp; if (nv != null) { yield return "nv|" + nv.Id.ToString("X8"); continue; }
                var dr = o as DeviceRegOp; if (dr != null) { yield return ("dev|" + dr.Label + "|" + dr.Name).ToLowerInvariant(); continue; }
                var dx = o as DxTokenOp; if (dx != null) { yield return ("dx|" + dx.Token).ToLowerInvariant(); continue; }
                var dd = o as DevDisableOp;
                if (dd != null) { foreach (var x in (dd.Ids ?? new string[0]).Concat(dd.Names ?? new string[0])) yield return ("devoff|" + x).ToLowerInvariant(); continue; }
                var na = o as NicAdvOp; if (na != null) { foreach (var r in na.Rules) yield return ("nic|" + r[0]).ToLowerInvariant(); continue; }
            }
            foreach (var c in t.ApplyCmds) yield return ("cmd|" + string.Join(" ", c)).ToLowerInvariant();
        }

        // called by Build after every tweak exists
        static void Sort36()
        {
            foreach (var t in all)
            {
                if (ToHardening.Contains(t.Id)) t.Category = HARDENING;
                if (t.Experimental) t.Presets = new string[0];   // never in a preset or an automatic pick
                if (t.Id == "gd_amd_ulps")
                {   // WinSux 2026: ULPS off hurts hardware GPU scheduling on RX 7000 / 9000; only offered for older Radeons
                    t.Requirement = Devices.HasOldAmdGpu; t.RequirementText = "an AMD GPU older than RX 7000";
                }
            }
        }

        static readonly Dictionary<string, object[]> GainsV36 = new Dictionary<string, object[]>
        {
            { "gd_nv_telemetry", new object[] { 0, "No FPS effect. A few fewer background tasks and no telemetry uploads. Driver-update checks no longer run daily." } },
            { "priv_autologgers", new object[] { 1, "Less background disk writing and a few DPCs less. Mostly privacy." } },
            { "gd_usb_stack", new object[] { 1, "No USB / HID wake-up stalls (≈5–50 ms) after idle. Consistency fix, not FPS." } },
            { "gd_dev_virtual", new object[] { 0, "A few drivers and interrupt sources less. Usually not measurable." } },
            { "gd_dev_nvaudio", new object[] { 0, "One audio endpoint less. No speed effect." } },
            { "gd_laptop_display", new object[] { 2, "Laptops: removes refresh-rate drops and PSR stutter, for steady frame pacing on the built-in screen." } },
            { "gd_nv_dlss_latest", new object[] { 0, "Better image quality. ≈2–5 % less FPS than the old DLSS model at the same preset." } },
            { "gd_nv_dlss_indicator", new object[] { 0, "Diagnostic overlay only." } },
            { "hd_ntlm", new object[] { 0, "Security only." } },
            { "hd_tls", new object[] { 0, "Security only." } },
            { "hd_dnsclient", new object[] { 0, "Privacy with VPNs. No speed effect." } },
            { "hd_icmpredirect", new object[] { 0, "Security only." } },
            { "hd_rdp", new object[] { 0, "Security only." } },
            { "hd_autorun", new object[] { 0, "Security only." } },
            { "hd_lmhosts", new object[] { 0, "Security only." } },
            { "dz_ntlm_out", new object[] { 0, "Security only." } },
            { "exp_serialtimer", new object[] { 1, "Unproven: reports range from smoother frame times to no change." } },
            { "exp_latencytol", new object[] { 1, "Unproven: can trim device wake-up delays. Often within noise." } },
            { "dz_nv_rmpower", new object[] { 1, "Unproven: a lower GPU DPC peak on some systems." } },
            { "gd_amd_lowlat", new object[] { 1, "Unproven: steadier GPU clocks between frames. Anti-Lag lowers latency when the GPU is the limit." } },
            { "dz_dev_hpet", new object[] { 0, "Usually none on modern systems; contested." } },
        };
    }
}
