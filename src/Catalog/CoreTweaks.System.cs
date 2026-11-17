// ArchTweaker: core tweak list, part 2 (3.32 split from CoreTweaks.cs): Network, Internet & Network, Responsiveness, Tasks & Services, Command tweaks.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

#pragma warning disable 219   // each part declares every category name, and uses only its own
namespace ArchTweaker
{
    public static partial class Catalog
    {
        static void AddCoreSystem()
        {
            string P = Cat.Privacy, I = Cat.Interface, E = Cat.Explorer, S = Cat.System, G = Cat.Gaming, N = Cat.Network, X = DangerZone.CAT;
            string RS = Cat.Responsiveness, TS = Cat.TasksServices, C = Cat.Commands;
            string safe = PRESET_SAFE, game = PRESET_GAMING, priv = PRESET_PRIVACY;

            // ---------------- NETWORK ----------------
            T("llmnr", N, "Disable LLMNR", "Closes a common LAN credential-sniffing vector.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast", 0, null).In(safe, priv);
            T("smb1", N, "Disable SMBv1 server", "Legacy, insecure file-sharing protocol (WannaCry).")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "SMB1", 0, null).NeedsReboot().In(safe, priv);
            T("ncsi", N, "Disable active connectivity probes (NCSI)", "Stops pinging msftconnecttest.com. Side effect: the network icon may show 'No internet' even though you're online.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\NetworkConnectivityStatusIndicator", "NoActiveProbe", 1, null)
                .R(Risk.Moderate);

            // ---------------- INTERNET & NETWORK (new) ----------------
            T("dnsneg", N, "Don't cache failed DNS lookups", "A site that was briefly down works again immediately, instead of staying 'not found' for up to 15 minutes.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "MaxNegativeCacheTtl", 0, null)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "NegativeCacheTime", 0, null)
                .In(safe, game);
            T("dnscloudflare", N, "Use Cloudflare DNS (1.1.1.1) on all adapters", "Usually faster, more private DNS than your ISP's. Reverting goes back to automatic (DHCP) DNS.")
                .PS("Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses ('1.1.1.1','1.0.0.1','2606:4700:4700::1111','2606:4700:4700::1001') }")
                .Cmd("ipconfig.exe", "/flushdns")
                .UndoPS("Get-NetAdapter -Physical | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses }")
                .In(game);
            T("doh", N, "Enable DNS over HTTPS (automatic)", "Windows encrypts DNS lookups for DNS servers it knows support it (Cloudflare, Google, Quad9). Your ISP can't see or tamper with them. Other servers keep plain DNS.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "DoHPolicy", 2, null)   // 3.7: current policy ("Configure DNS over HTTPS": allow); EnableAutoDoh was the 2020 Insider switch
                .W11().In(priv, game);
            T("preferipv4", N, "Prefer IPv4 over IPv6", "A Microsoft-documented setting. IPv6 stays on, but Windows tries IPv4 first. This fixes slow connects on networks with broken IPv6.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", "DisabledComponents", 0x20, null)
                .NeedsReboot().R(Risk.Moderate);
            T("maxconn", N, "More parallel connections per server", "Raises the WinINet limit (2 → 16) for apps using the Windows internet stack. Faster multi-file downloads.")
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "MaxConnectionsPerServer", 16, null)
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "MaxConnectionsPer1_0Server", 16, null)
                .In(safe, game);
            T("wubandwidth", N, "Limit Windows Update background bandwidth to 30%", "Updates can't saturate your connection while you game or stream.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DOPercentageMaxBackgroundBandwidth", 30, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DOPercentageMaxForegroundBandwidth", 60, null)
                .In(game);
            T("nicpower", N, "Stop Windows from powering down network adapters", "Prevents the NIC/Wi-Fi from sleeping (lag spikes and disconnects after idle).")
                .PS("Get-NetAdapter -Physical | ForEach-Object { Disable-NetAdapterPowerManagement -Name $_.Name -ErrorAction SilentlyContinue }")
                .UndoPS("Get-NetAdapter -Physical | ForEach-Object { Enable-NetAdapterPowerManagement -Name $_.Name -ErrorAction SilentlyContinue }")
                .In(game);
            T("netbios", N, "Disable NetBIOS over TCP/IP", "Legacy name-resolution protocol and a LAN attack surface. Keep it on if you browse very old NAS/SMB1 shares by name.")
                .Explains("For every adapter under HKLM\\SYSTEM\\CurrentControlSet\\Services\\NetBT\\Parameters\\Interfaces: NetbiosOptions = 2 (disabled)   ·   undo: 0 (DHCP default)")
                .DetectBy(() => NetBios.IsDisabled())
                .OnApply(() => NetBios.Set(true)).OnRevert(() => NetBios.Set(false))
                .In(priv);
            T("mdnsoff", N, "Disable multicast DNS (mDNS) responder", "Your PC stops announcing itself on the LAN. Breaks discovery of some printers and Chromecasts.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "EnableMDNS", 0, null)
                .R(Risk.Moderate);

            // ---------------- RESPONSIVENESS (new) ----------------
            T("startupdelay", RS, "Remove startup-apps delay", "Windows normally waits ~10 s after sign-in before launching startup apps. This removes the wait.")
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0, null)
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "WaitForIdleState", 0, null)
                .In(safe, game);
            T("hovertime", RS, "Faster hover tooltips & taskbar previews", "Tooltips and taskbar thumbnails appear almost instantly.")
                .Sz(CU, @"Control Panel\Mouse", "MouseHoverTime", "10", "400")
                .Dw(CU, ADV, "ExtendedUIHoverTime", 100, null)
                .NeedsRelogon().In(safe, game);
            T("menudelay", RS, "Instant menus (MenuShowDelay 0)", "Cascading menus open with no 400 ms delay.")
                .Sz(CU, @"Control Panel\Desktop", "MenuShowDelay", "0", "400").NeedsRelogon().In(safe, game);
            T("keyrepeat", RS, "Fastest keyboard repeat", "Shortest repeat delay and fastest repeat rate.")
                .Sz(CU, @"Control Panel\Keyboard", "KeyboardDelay", "0", "1")
                .Sz(CU, @"Control Panel\Keyboard", "KeyboardSpeed", "31", "31")
                .NeedsRelogon().In(game);
            T("folderdisc", RS, "Disable automatic folder-type discovery", "Explorer stops scanning folder contents to pick a view. Big folders like Downloads open much faster.")
                .Sz(CU, @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell", "FolderType", "NotSpecified", null)
                .NeedsRelogon().In(safe, game);
            T("sepprocess", RS, "Open folder windows in a separate process", "A crashing folder window or shell extension no longer takes down the taskbar and desktop.")
                .Dw(CU, ADV, "SeparateProcess", 1, 0).NeedsRelogon().In(safe);
            T("hooktimeout", RS, "Shorter low-level hook timeout", "Stops a frozen app with a keyboard/mouse hook from freezing your input for up to 5 seconds.")
                .Sz(CU, @"Control Panel\Desktop", "LowLevelHooksTimeout", "1000", null).NeedsRelogon().In(game);
            T("svchostsplit", RS, "Group svchost services (fewer processes)", "Sets the svchost split threshold to your RAM size, cutting about 80 svchost.exe processes down to about 30. Lowers overhead. Needs a restart.")
                .Explains("Registry: HKLM\\SYSTEM\\CurrentControlSet\\Control\\SvcHostSplitThresholdInKB = your installed RAM in KB (" + Mem.TotalKB() + ")   ·   undo: 3670016 (Windows default)")
                .DetectBy(() => { object o = Reg.Get(LM, @"SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB"); return o is int && (uint)(int)o >= Mem.TotalKB() && Mem.TotalKB() > 0; })
                .OnApply(() => Reg.Set(LM, @"SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", RegistryValueKind.DWord, (int)Math.Min(int.MaxValue, Mem.TotalKB() + 1024)))
                .OnRevert(() => Reg.Set(LM, @"SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", RegistryValueKind.DWord, 0x380000))
                .NeedsReboot().R(Risk.Moderate).In(game);
            T("pagingexec", RS, "Keep kernel & drivers in RAM", "DisablePagingExecutive: kernel code is never paged to disk. Only for 16 GB+ RAM.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive", 1, 0)
                .NeedsReboot().R(Risk.Moderate).In(game);
            T("fth", RS, "Disable Fault Tolerant Heap", "Windows stops silently 'shimming' apps that crashed before. Those shims can slow the app down for good.")
                .Dw(LM, @"SOFTWARE\Microsoft\FTH", "Enabled", 0, 1).R(Risk.Moderate);
            T("aeroshake", RS, "Disable Aero Shake", "Shaking a window no longer minimises all the others.")
                .Dw(CU, ADV, "DisallowShaking", 1, null).In(safe);
            T("searchhl", RS, "Disable search highlights", "Removes the animated doodles and trending content in Start search.")
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", 0, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "EnableDynamicContentInWSB", 0, null)
                .In(safe, priv, game);
            T("ntfsmem", RS, "Larger NTFS memory cache (fsutil memoryusage 2)", "More paged pool for NTFS metadata, which helps with lots of small files. Only for 16 GB+ RAM.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsMemoryUsage", 2, null)
                .NeedsReboot().R(Risk.Moderate);
            T("memcompress", RS, "Disable memory compression", "Saves CPU work on systems with plenty of RAM (32 GB+). Leave it on with 16 GB or less.")
                .PS("Disable-MMAgent -MemoryCompression").UndoPS("Enable-MMAgent -MemoryCompression")
                .NeedsReboot().R(Risk.Moderate);

            // ---------------- TASKS & SERVICES (new) ----------------
            T("tasks_telemetry", TS, "Disable telemetry scheduled tasks", "Compatibility Appraiser, CEIP, disk diagnostics, feedback, error-reporting queue, device census and similar. These are the main background telemetry jobs.")
                .Tasks(@"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
                       @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
                       @"\Microsoft\Windows\Application Experience\PcaPatchDbTask",
                       @"\Microsoft\Windows\Application Experience\MareBackup",
                       @"\Microsoft\Windows\Autochk\Proxy",
                       @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
                       @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
                       @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
                       @"\Microsoft\Windows\Feedback\Siuf\DmClient",
                       @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload",
                       @"\Microsoft\Windows\Windows Error Reporting\QueueReporting",
                       @"\Microsoft\Windows\Device Information\Device",
                       @"\Microsoft\Windows\Device Information\Device User",
                       @"\Microsoft\Windows\PI\Sqm-Tasks",
                       @"\Microsoft\Windows\NetTrace\GatherNetworkInfo",
                       @"\Microsoft\Windows\CloudExperienceHost\CreateObjectTask",
                       @"\Microsoft\Windows\Flighting\OneSettings\RefreshCache")
                .In(safe, priv, game);
            T("tasks_misc", TS, "Disable non-essential scheduled tasks", "Maps updates, Media Player library sharing, retail demo, power-efficiency diagnostics, family safety monitor.")
                .Tasks(@"\Microsoft\Windows\Maps\MapsUpdateTask",
                       @"\Microsoft\Windows\Maps\MapsToastTask",
                       @"\Microsoft\Windows\Windows Media Sharing\UpdateLibrary",
                       @"\Microsoft\Windows\RetailDemo\CleanupOfflineContent",
                       @"\Microsoft\Windows\Power Efficiency Diagnostics\AnalyzeSystem",
                       @"\Microsoft\Windows\Shell\FamilySafetyMonitor",
                       @"\Microsoft\Windows\Shell\FamilySafetyRefreshTask")
                .In(safe, game);
            T("tasks_xbox", TS, "Disable Xbox cloud-save task", "Only if you don't use Xbox / Game Pass cloud saves on PC.")
                .Tasks(@"\Microsoft\XblGameSave\XblGameSaveTask").R(Risk.Moderate);
            T("svc_misc", TS, "Disable non-essential services", "Maps, retail demo, fax, Media Player sharing, parental controls, NFC payments, Insider service, AllJoyn, wallet, link tracking. Nothing most desktops ever use.")
                .Service("MapsBroker", 4, 2, true).Service("RetailDemo", 4, 3).Service("Fax", 4, 3).Service("WMPNetworkSvc", 4, 3)
                .Service("WpcMonSvc", 4, 3).Service("SEMgrSvc", 4, 3).Service("wisvc", 4, 3).Service("AJRouter", 4, 3)
                .Service("WalletService", 4, 3).Service("TrkWks", 4, 2).Service("RemoteRegistry", 4, 4)
                .In(safe, game);
            T("svc_hyperv", TS, "Disable Hyper-V guest integration services", "These only do anything when Windows runs inside a virtual machine. Safe on a physical PC.")
                .Service("vmickvpexchange", 4, 3).Service("vmicguestinterface", 4, 3).Service("vmicshutdown", 4, 3).Service("vmicheartbeat", 4, 3)
                .Service("vmicvmsession", 4, 3).Service("vmicrdv", 4, 3).Service("vmictimesync", 4, 3).Service("vmicvss", 4, 3)
                .In(game);
            T("svc_remote", TS, "Disable Remote Desktop & remote access services", "Turn this off if you never connect to this PC remotely (RDP).")
                .Service("TermService", 4, 3).Service("SessionEnv", 4, 3).Service("UmRdpService", 4, 3).Service("RemoteAccess", 4, 4)
                .R(Risk.Moderate);
            T("svc_print", TS, "Disable printing services", "Only if you have no printer and don't use 'Print to PDF'.")
                .Service("Spooler", 4, 2).Service("PrintNotify", 4, 3).R(Risk.Moderate);
            T("svc_xbox", TS, "Disable Xbox services", "Breaks the Xbox app, Game Pass and Xbox controller pairing features. Keep these if you use them.")
                .Service("XblAuthManager", 4, 3).Service("XblGameSave", 4, 3).Service("XboxNetApiSvc", 4, 3).Service("XboxGipSvc", 4, 3)
                .R(Risk.Moderate);
            T("svc_bt", TS, "Disable Bluetooth services", "Only on PCs with no Bluetooth headsets, controllers or mice.")
                .Service("bthserv", 4, 3).Service("BTAGService", 4, 3).Service("BthAvctpSvc", 4, 3).R(Risk.Moderate);
            T("svc_diag", TS, "Disable diagnostic services", "Diagnostic Policy and host services. Windows troubleshooters and 'Network diagnostics' stop working.")
                .Service("DPS", 4, 2).Service("WdiServiceHost", 4, 3).Service("WdiSystemHost", 4, 3).Service("diagsvc", 4, 3).Service("diagnosticshub.standardcollector.service", 4, 3)
                .R(Risk.Moderate).In(priv);
            T("svc_touch", TS, "Disable touch keyboard & handwriting service", "Desktop without touchscreen only. Also disables the Win+. emoji panel on some builds.")
                .Service("TabletInputService", 4, 3).R(Risk.Moderate);
            T("svc_cdp", TS, "Disable Connected Devices Platform", "Turns off Nearby Sharing, Phone Link and cross-device features.")
                .Service("CDPSvc", 4, 2, true).R(Risk.Moderate).In(priv);

            // ---------------- COMMAND TWEAKS (new) ----------------
            T("cmd_dynamictick", C, "bcdedit: disable dynamic tick", "The timer keeps ticking at a fixed rate instead of pausing at idle. Smoother frame pacing on some systems, slightly higher idle power.")
                .Cmd("bcdedit.exe", "/set disabledynamictick yes").UndoCmd("bcdedit.exe", "/deletevalue disabledynamictick")
                .NeedsReboot().R(Risk.Moderate).In(game);
            T("cmd_bootmenu", C, "bcdedit: classic F8 boot menu", "Lets you press F8 at boot to reach Safe Mode.")
                .Cmd("bcdedit.exe", "/set {default} bootmenupolicy legacy").UndoCmd("bcdedit.exe", "/set {default} bootmenupolicy standard");
            T("cmd_hypervisor", C, "bcdedit: don't start the hypervisor", "Can lower latency and raise FPS a little. Breaks WSL2, Docker, Hyper-V, Windows Sandbox, Memory Integrity, and anti-cheats that need VBS.")
                .Cmd("bcdedit.exe", "/set hypervisorlaunchtype off").UndoCmd("bcdedit.exe", "/set hypervisorlaunchtype auto")
                .NeedsReboot().R(Risk.Advanced);
            T("cmd_trim", C, "fsutil: make sure SSD TRIM is enabled", "Keeps SSD write speed high over time. Turns TRIM on for NTFS and ReFS if a tool turned it off.")
                .Cmd("fsutil.exe", "behavior set disabledeletenotify NTFS 0").Cmd("fsutil.exe", "behavior set disabledeletenotify ReFS 0")
                .In(safe, game);
            T("cmd_autotuning", C, "netsh: TCP auto-tuning = normal", "Restores the TCP receive window to scale automatically. Old 'tweaks' that disable it cap download speed.")
                .Cmd("netsh.exe", "int tcp set global autotuninglevel=normal")
                .In(safe, game);
            T("cmd_rss", C, "netsh: enable Receive-Side Scaling", "Spreads network interrupts across CPU cores.")
                .Cmd("netsh.exe", "int tcp set global rss=enabled").In(game);
            T("cmd_heuristics", C, "netsh: disable TCP window-scaling heuristics", "Stops Windows from deciding on its own to shrink the TCP window on 'unreliable' networks.")
                .Cmd("netsh.exe", "int tcp set heuristics disabled").UndoCmd("netsh.exe", "int tcp set heuristics default").In(game);
            T("cmd_fastopen", C, "netsh: enable TCP Fast Open", "Saves one round trip when reconnecting to servers that support it.")
                .Cmd("netsh.exe", "int tcp set global fastopen=enabled").Cmd("netsh.exe", "int tcp set global fastopenfallback=enabled")
                .UndoCmd("netsh.exe", "int tcp set global fastopen=disabled").In(game);
            T("cmd_teredo", C, "netsh: disable Teredo tunnelling", "Removes an IPv6 tunnelling adapter. Some Xbox / Game Pass multiplayer features need Teredo.")
                .Cmd("netsh.exe", "interface teredo set state disabled").UndoCmd("netsh.exe", "interface teredo set state default")
                .R(Risk.Moderate);
            T("cmd_dupplans", C, "powercfg: delete duplicate power plans", "One-time cleanup of duplicate plans left by tools and drivers. Keeps the active, Balanced, High and Ultimate plans.")
                .Explains("Runs powercfg /list, then powercfg /delete for plans whose name is a duplicate. Built-in plans are never deleted. This can't be undone, but you can re-create plans with powercfg -restoredefaultschemes.")
                .OnApply(PowerPlans.RemoveDuplicates);
        }
    }
}
