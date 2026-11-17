// 3.6: for every tweak, in plain words: what it switches off, and what it can break.
// Shown on the row ("Turns off:" / "Can break:"), in the details panel and in the review.
// Danger zone tweaks keep their own Breaks text (Danger.cs); a tweak that sets its own text keeps it.
using System.Collections.Generic;

namespace ArchTweaker
{
    public static partial class Catalog
    {
        // id → { turns off, can break }   (null = nothing to say)
        static readonly Dictionary<string, string[]> Effects = new Dictionary<string, string[]>
        {
            // ---- Privacy
            { "telemetry", new[] { "Optional diagnostic data, the Connected User Experiences (DiagTrack) and WAP push services.", "Windows Insider builds, which need optional diagnostic data." } },
            { "adid", new[] { "The advertising ID apps use for personalised ads.", null } },
            { "activity", new[] { "Activity history (Timeline) and its upload.", "Resuming tasks from activity history on another PC." } },
            { "tailored", new[] { "Tips, ads and recommendations based on your diagnostic data.", null } },
            { "feedback", new[] { "Windows feedback pop-ups.", null } },
            { "websearch", new[] { "Bing web results and suggestions in Start search.", "Searching the web from Start: use the browser instead." } },
            { "cortana", new[] { "Cortana.", null } },
            { "suggestions", new[] { "Suggestions in Start, Settings and the lock screen, and silent install of promoted apps.", null } },
            { "trackprogs", new[] { "App-launch tracking for Start.", "The 'Most used' list in Start." } },
            { "inking", new[] { "Collection of typing and handwriting samples.", "Handwriting and text prediction personalised to you." } },
            { "location", new[] { "Location for every app and Windows.", "Find My Device, automatic time zone, weather and maps by location." } },
            { "wer", new[] { "Windows Error Reporting.", "Microsoft and app makers no longer receive your crash reports, so fixes for them may come later." } },
            { "ceip", new[] { "The Customer Experience Improvement Program.", null } },
            { "copilot", new[] { "Windows Copilot.", "Copilot in Windows (the separate Copilot app still works if installed)." } },
            { "recall", new[] { "Recall screen snapshots.", "Searching your past screen activity with Recall." } },
            { "chromebg", new[] { "Chrome running after you close it, and Chrome usage statistics.", "Chrome notifications and apps when you close Chrome." } },
            { "bravebg", new[] { "Brave running after you close it, and Brave usage statistics.", "Brave notifications when you close Brave." } },
            { "edgeprelaunch", new[] { "Edge pre-launch at sign-in and running after you close it.", "Edge starts a little slower the first time." } },
            { "priv_backgroundapps", new[] { "Store apps running in the background.", "Live notifications and background sync of Store apps (Mail, Phone Link, Store-version chat apps) until you open them." } },
            { "ro_norecentdocs", new[] { "The recent documents list.", "Recent files in Start, Quick Access and jump lists." } },
            { "ro_openwithweb", new[] { "Store and web lookup in 'Open with'.", null } },
            { "priv_cloudsearch", new[] { "Cloud results (OneDrive, Outlook, SharePoint) and search history in Windows Search.", "Finding your cloud files and mail from Start search." } },
            { "priv_paintai", new[] { "Cocreator, Image Creator and generative fill in Paint.", null } },
            { "priv_lockscreen", new[] { "Windows Spotlight pictures, tips and fun facts on the lock screen.", null } },
            { "priv_locktoasts", new[] { "Notifications on the lock screen.", "Seeing calls, alarms and messages without unlocking." } },
            { "priv_devmeta", new[] { "Downloading device names, pictures and info.", "Device pictures in Settings → Bluetooth & devices may be generic." } },
            { "priv_ink", new[] { "Windows Ink Workspace.", "The pen-button shortcuts to the sketch and sticky-note workspace." } },
            { "priv_phonelink", new[] { "Phone Link, resuming phone apps on the PC and cross-device features.", "Texts, calls and photos from your phone on the PC." } },
            { "priv_accountnotif", new[] { "Account, backup and OneDrive reminders in Start and Settings.", null } },
            { "priv_tips", new[] { "Online tips, the 'finish setting up' screen, typing insights, smart clipboard actions and most-used-app tracking.", null } },
            { "priv_langlist", new[] { "Sharing your language list with websites.", "Some sites no longer pick your language on their own." } },
            { "priv_pstelemetry", new[] { "PowerShell 7 telemetry.", null } },
            { "priv_perm_personal", new[] { "Store-app access to contacts, calendar, email, calls, messages, tasks, notifications and account info.", "Store apps that need them (Mail, Calendar, People, Phone Link) until you allow each one in Settings → Privacy." } },
            { "priv_perm_files", new[] { "Store-app access to your libraries, Downloads and the file system.", "Photos, Media Player and other Store apps can't show your files until you allow them." } },
            { "priv_perm_device", new[] { "Store-app access to diagnostics, radios, unpaired-device sync and border-free capture.", "Store apps that switch Bluetooth / Wi-Fi or capture the screen, until you allow them." } },

            // ---- Interface / Explorer
            { "transparency", new[] { "Transparency (acrylic and mica) effects.", null } },
            { "animations", new[] { "Minimise / maximise and taskbar animations.", null } },
            { "taskview", new[] { "The Task View button.", null } },
            { "widgets", new[] { "The Widgets board, News and Interests, and their background process.", "Widgets and the weather on the taskbar." } },
            { "chaticon", new[] { "The Chat (Teams) taskbar button.", null } },
            { "searchbox", new[] { "The taskbar search box.", null } },
            { "lockscreen", new[] { "The lock screen picture before sign-in.", null } },
            { "stickykeys", new[] { "The Sticky Keys shortcut (Shift ×5).", "Turning on Sticky Keys from the keyboard: use Settings → Accessibility." } },
            { "startrec", new[] { "Recent files and frequent folders in Start, Jump Lists and Quick Access.", null } },
            { "3dobjects", new[] { "The 3D Objects folder in This PC.", null } },
            { "noshare", new[] { "'Share' and 'Give access to' in the right-click menu.", null } },
            { "nocast", new[] { "'Cast to Device' in the right-click menu.", null } },
            { "noprevver", new[] { "'Restore previous versions' in menus and Properties.", null } },
            { "classicmenu", new[] { "The Windows 11 short context menu.", null } },
            { "ui_snap", new[] { "Snap-layout flyout and the snap bar while dragging.", null } },
            { "ui_startrec", new[] { "Start's Recommended section and its suggestions.", null } },
            { "ui_copilotbtn", new[] { "The Copilot taskbar button.", null } },
            { "ui_dynlight", new[] { "Windows Dynamic Lighting.", "RGB control from Windows Settings: use your vendor's app." } },
            { "ui_langhotkey", new[] { "Alt+Shift and Ctrl+Shift layout switching.", "Switching layouts with those keys: use Win+Space." } },
            { "disp_fontsmooth_std", new[] { "ClearType sub-pixel text.", "Text looks a little softer on regular LCDs." } },
            { "disp_iconsonly", new[] { "Picture and video thumbnails in Explorer.", "Previews of photos and videos in folders." } },
            { "disp_thumbsdb_net", new[] { "thumbs.db files on network folders.", null } },

            // ---- System
            { "hibernate", new[] { "Hibernation and Fast Startup (ArchTweaker deletes hiberfil.sys).", "Hibernate on laptops: an empty battery while asleep means lost work." } },
            { "faststartup", new[] { "Fast Startup (hybrid boot).", "Cold boots take a few seconds longer." } },
            { "dop2p", new[] { "Uploading Windows updates to other PCs.", null } },
            { "nodrivers", new[] { "Driver installs through Windows Update.", "New devices may need their driver installed by hand." } },
            { "noautoreboot", new[] { "Forced update restarts while you're signed in.", null } },
            { "wunotify", new[] { "Automatic download and install of updates.", "Security updates arrive only when you install them yourself." } },
            { "lastaccess", new[] { "NTFS last-access time stamps.", "Backup or cleanup tools that sort by 'last opened'." } },
            { "8dot3", new[] { "8.3 short names for new files.", "Very old 16-bit era installers that need short names." } },
            { "sysmain", new[] { "SysMain (Superfetch).", "Faster app launches on hard disks (none on an SSD)." } },
            { "wsearch", new[] { "Windows Search indexing.", "Searching file contents and Outlook mail becomes slow." } },
            { "reservedstorage", new[] { "Reserved Storage (~7 GB).", "Updates can fail when the disk is almost full." } },
            { "storagesense", new[] { "Storage Sense automatic cleanup.", null } },
            { "sys_maintenance", new[] { "Automatic idle maintenance.", "Scheduled defrag / TRIM, diagnostics and some update steps until you run them by hand." } },
            { "sys_wpbt", new[] { "Programs the motherboard firmware asks Windows to run (WPBT).", "Vendor tools that install themselves this way (for example Armoury Crate)." } },
            { "sys_pauseupdates", new[] { "Windows Update for one year.", "All security fixes until you resume updates." } },
            { "sys_mmagent", new[] { "Prefetching, app pre-launch and page combining.", "Faster app launches on hard disks." } },
            { "sys_archiving", new[] { "Automatic archiving of unused Store apps.", null } },
            { "sys_storeauto", new[] { "Automatic Store app updates.", "Store apps and Game Pass games stay on old versions until you update them." } },
            { "sys_featurelock", new[] { "The next Windows feature update.", "New Windows features, and support ends when this version reaches end of service." } },
            { "uacdim", new[] { "The dimmed secure desktop behind UAC prompts.", "Some protection against programs faking or clicking a UAC prompt." } },
            { "ro_nolowdisk", new[] { "The low disk space warning.", null } },
            { "ro_shortcutresolve", new[] { "Searching drives and the network for a broken shortcut's target.", null } },
            { "ro_dnscache", new[] { "Nothing switched off: the DNS client keeps more names in its cache.", "Windows can take longer to notice a changed website address." } },

            // ---- Responsiveness
            { "folderdisc", new[] { "Automatic folder-type detection.", null } },
            { "aeroshake", new[] { "Aero Shake.", null } },
            { "searchhl", new[] { "Search highlights.", null } },
            { "rs_visualfx", new[] { "Fading and sliding menus, shadows, window contents while dragging, and translucent selection.", null } },
            { "rs_inputpreload", new[] { "Pre-loading of the touch keyboard / emoji host and widgets.", "The emoji panel (Win+.) opens a moment slower the first time." } },
            { "svchostsplit", new[] { "Nothing switched off: services share fewer svchost processes.", "One crashing service can take down the others in its shared svchost." } },
            { "pagingexec", new[] { "Paging kernel and driver code to disk.", "Uses more RAM: avoid with less than 16 GB." } },
            { "fth", new[] { "The Fault Tolerant Heap.", "Apps that crash often get no automatic protection from it." } },
            { "ntfsmem", new[] { "Nothing switched off: NTFS gets a larger paged-pool cache.", "Uses more kernel memory: avoid with less than 16 GB." } },
            { "memcompress", new[] { "Memory compression.", "With 16 GB or less, more paging to disk." } },

            // ---- Gaming
            { "gamedvr", new[] { "Game DVR background recording.", "Recording the last minutes of play with Game Bar." } },
            { "gamebar", new[] { "The Xbox Game Bar overlay (Win+G).", "Game Bar widgets, its FPS counter and capture." } },
            { "fso", new[] { "Fullscreen optimisations.", "On Windows 11, Auto HDR and fast Alt+Tab in games that relied on them." } },
            { "netthrottle", new[] { "The multimedia network throttle.", "Can raise network DPC latency on some adapters. Audio or video streams can stutter during heavy downloads." } },
            { "shortcutsuffix", new[] { "The ' - Shortcut' text added to new shortcut names.", null } },
            { "startupdelay", new[] { "The ~10 s wait before startup apps launch.", "Many startup apps start at once and compete for the disk right after sign-in." } },
            { "powerthrottle", new[] { "Power Throttling (efficiency mode for background apps).", "Laptop battery life." } },
            { "mouseaccel", new[] { "Enhance pointer precision (mouse acceleration).", null } },
            { "mpo", new[] { "Multiplane overlay (MPO).", "Some GPU power saving with video playback, and Auto HDR / windowed independent flip on some drivers." } },
            { "nagle", new[] { "Nagle's algorithm (TCP packet batching).", "Slightly more packets on very chatty TCP apps." } },
            { "gamebar_links", new string[] { null, "If you reinstall Game Bar, untick this first or its links stay redirected." } },
            { "mmcss_latency", new[] { "MMCSS idling between bursts.", "A little more idle power." } },

            // ---- Internet & Network / Hardening
            { "llmnr", new[] { "LLMNR name lookups.", "Finding PCs by name on networks without DNS (rare at home)." } },
            { "smb1", new[] { "The SMBv1 file-sharing server.", "Very old devices that browse shares on this PC with SMBv1." } },
            { "ncsi", new[] { "Internet connectivity probes.", "The network icon may show 'No internet', and captive Wi-Fi login pages may not open." } },
            { "dnsneg", new[] { "Caching failed DNS lookups.", null } },
            { "preferipv4", new[] { "IPv6 as the first choice for connections (IPv6 itself still works).", "Very rarely: services that answer properly only over IPv6 connect a little slower." } },
            { "net_ports", new string[] { null, null } },
            { "dnscloudflare", new[] { "Your provider's DNS.", "Local names your router serves (some NAS and printer names), and provider parental filters." } },
            { "nicpower", new[] { "Network adapter sleep.", "Wake-on-LAN and some laptop battery life." } },
            { "netbios", new[] { "NetBIOS over TCP/IP.", "Browsing very old NAS / SMB1 shares by name." } },
            { "mdnsoff", new[] { "The mDNS responder.", "Discovery of some printers, Chromecasts and AirPlay speakers." } },
            { "net_remoteassist", new[] { "Remote Assistance.", "Someone helping you through Windows Remote Assistance (Quick Assist still works)." } },
            { "net_bindings", new[] { "LLDP, link-layer topology discovery and the adapter multiplexor on your adapters.", "The network map and NIC teaming." } },
            { "net_pacer", new[] { "The QoS Packet Scheduler on your adapters.", "Windows QoS policies and DSCP marking. Router QoS that reads those marks." } },
            { "dz_cfg", new[] { "Control Flow Guard checks in every program built with CFG.", "Exploit protection against memory-corruption attacks. Some anti-cheats need CFG." } },
            { "net_nicadv", new[] { "Flow control, large-send offload and receive coalescing on the adapter.", "More CPU load at multi-gigabit speeds. Rare packet drops on very busy links." } },
            { "net_nicpower2", new[] { "Adapter power-down offloads, selective suspend, gigabit-off and wake on PME.", "Wake-on-LAN and some laptop battery life." } },
            { "doh", new string[] { null, "Corporate DNS filtering that relies on plain DNS." } },
            { "wubandwidth", new string[] { null, "Updates download more slowly in the background." } },

            // ---- Tasks & Services
            { "tasks_telemetry", new[] { "Compatibility Appraiser, CEIP, disk diagnostics, feedback, error queue, device census.", "Windows 11 upgrade compatibility checks until you run them by hand." } },
            { "tasks_misc", new[] { "Maps updates, Media Player sharing, retail demo, power-efficiency diagnostics and family safety tasks.", "Offline maps updates and Microsoft Family monitoring." } },
            { "tasks_xbox", new[] { "The Xbox cloud-save task.", "Xbox / Game Pass cloud saves on PC." } },
            { "svc_misc", new[] { "Maps, retail demo, fax, Media Player sharing, parental controls, NFC payments, Insider, AllJoyn, wallet and link tracking.", "Offline maps, parental controls, NFC payments and the Insider program." } },
            { "svc_hyperv", new[] { "Hyper-V guest integration services.", "Only matters if this Windows runs inside a virtual machine." } },
            { "svc_remote", new[] { "Remote Desktop and remote access services.", "Connecting to this PC with Remote Desktop, and some VPN clients." } },
            { "svc_print", new[] { "The print spooler and printing services.", "All printing, including Print to PDF." } },
            { "svc_xbox", new[] { "Xbox Live auth, Game Save, accessory management and networking services.", "The Xbox app, Game Pass, cloud saves and Xbox controller features." } },
            { "svc_bt", new[] { "Bluetooth services.", "Every Bluetooth headset, controller, mouse and phone link." } },
            { "svc_diag", new[] { "Diagnostic Policy and host services.", "Windows troubleshooters and network diagnostics." } },
            { "svc_touch", new[] { "The touch keyboard and handwriting service.", "Touch keyboard, pen handwriting and on some builds the emoji panel." } },
            { "svc_cdp", new[] { "The Connected Devices Platform.", "Nearby Sharing, Phone Link and clipboard sync between PCs." } },
            { "svc_killer", new[] { "Killer traffic shaping, analytics and the Killer app services.", "The Killer Control Center and its traffic priorities." } },
            { "svc_tier_safe", new[] { "About 30 rarely used services: smart cards, phone / SMS relay, peer-to-peer, Wi-Fi Direct and casting, distributed transactions, performance logs.", "Miracast / wireless display casting, smart-card sign-in, and apps that use distributed transactions (some old business software)." } },

            // ---- Commands
            { "cmd_dynamictick", new[] { "The dynamic (tickless) timer at idle.", "A little more idle power and laptop battery life." } },
            { "cmd_heuristics", new[] { "TCP window-scaling heuristics.", null } },
            { "cmd_teredo", new[] { "The Teredo IPv6 tunnel.", "Some Xbox / Game Pass multiplayer features (strict NAT)." } },
            { "cmd_rsc", new[] { "Receive segment coalescing.", "Slightly more CPU on very fast downloads." } },
            { "cmd_dupplans", new[] { "Duplicate power plans (deleted).", "Custom plans you made that have the same name as a built-in one." } },

            // ---- Power plan
            { "pp_minmax", new[] { "CPU clock drop to minimum between frames.", "Laptop battery life; slightly higher idle temperature." } },
            { "pp_parking", new[] { "CPU core parking.", "A little idle power." } },
            { "pp_nothrottle", new[] { "T-state throttling and duty cycling.", "Laptops run warmer and louder." } },
            { "pp_hybrid", new[] { "Windows' choice of E-cores for background threads.", "Background work also runs on P-cores: more power and heat on laptops." } },
            { "pp_pcie", new[] { "PCIe link power saving (ASPM).", "A few watts at idle, more on laptops." } },
            { "pp_usb", new[] { "USB selective suspend and USB3 link power saving.", "Laptop battery life." } },
            { "pp_disk", new[] { "Disk spin-down and SATA / NVMe link sleep.", "Hard drives keep spinning (noise, wear)." } },
            { "pp_wifi", new[] { "Wi-Fi power saving.", "Laptop battery life." } },
            { "pp_sleep", new[] { "Sleep, hibernate, hybrid sleep and wake timers.", "The PC never sleeps by itself. Overnight updates can't wake it." } },
            { "pp_standby", new[] { "Away mode and networking in standby.", "Notifications and downloads while a Modern Standby PC is asleep." } },
            { "pp_display", new[] { "Display dimming and adaptive brightness.", null } },
            { "pp_nvme_apst", new[] { "NVMe idle power states.", "A watt or two at idle. Laptops lose some battery life." } },
            { "pp_deepsleep", new[] { "Idle-resiliency deep sleep.", "Laptop battery life while the screen is on and idle." } },

            // ---- Input & latency
            { "il_queue", new[] { "Nothing switched off: mouse and keyboard buffers get smaller.", "Fast typists or 8 kHz mice can drop input if the buffer is too small." } },
            { "il_rawthrottle", new[] { "Windows 11's background raw-input throttle.", "Background apps use more CPU with high-polling mice." } },
            { "il_usbpower", new[] { "USB and HID device power saving.", "Laptop battery life." } },
            { "il_timers", new[] { "Timer expiry handled only by CPU 0.", "Undocumented: on a few systems it can raise DPC latency instead. Measure it." } },
            { "il_mouse_default", new[] { "Mouse acceleration on the sign-in screen and for new accounts.", null } },

            // ---- GPU & devices
            { "gd_msi", new[] { "Line-based GPU interrupts.", "Very old GPUs / drivers without MSI support can fail to start (Undo fixes it)." } },
            { "gd_nv_p0", new[] { "GPU downclocking at idle.", "Idle power +20–60 W, more heat and fan noise, and shorter GPU boost under load on some cards." } },
            { "gd_nv_hdcp", new[] { "HDCP copy protection.", "Netflix, Disney+, Blu-ray and other protected video in HD or at all." } },
            { "gd_nv_misc", new[] { "The NVIDIA tray icon, and PhysX auto-selection.", null } },
            { "gd_nv_telemetry", new[] { "NVIDIA telemetry, crash reports and automatic driver / profile update checks.", "Automatic driver-update notices: update by hand." } },
            { "gd_nv_drs_power", new[] { "Driver power saving in games.", "Higher GPU power and heat in light games." } },
            { "gd_nv_drs_frl", new[] { "Frame rates above your refresh rate.", "Max FPS for competitive play without G-SYNC." } },
            { "gd_nv_drs_texture", new[] { "Full-quality texture filtering.", "Distant textures can shimmer a little more." } },
            { "gd_nv_drs_misc", new[] { "Ansel, driver FXAA, Smooth Motion, the CUDA P2 memory cap and the driver watermark.", "Ansel photo mode and Smooth Motion frame generation." } },
            { "gd_nv_drs_vsync", new[] { "V-Sync and G-SYNC.", "Tear-free play: G-SYNC users should leave this off." } },
            { "gd_amd_ulps", new[] { "AMD Ultra Low Power State.", "Idle power. On RX 7000 / 9000 cards it can hurt hardware GPU scheduling." } },
            { "gd_pcipower", new[] { "PCI and ACPI device power saving.", "A few watts at idle. Laptops lose battery life." } },
            { "gd_wake", new[] { "Waking the PC from USB, HID, PCI and ACPI devices.", "Waking the PC with the keyboard, mouse or Wake-on-LAN." } },
            { "gd_nicpower", new[] { "Network adapter power saving, Green Ethernet and wake.", "Wake-on-LAN." } },
            { "gd_flip_composed", new[] { "V-sync waiting for hardware-composed independent flip.", "On some drivers: flicker or tearing in windowed apps." } },
            { "gd_intel_latency", new[] { "V-sync waiting on Intel integrated graphics (async flip instead).", "Intel iGPU only: tearing in some games." } },
            { "gd_audio_power", new[] { "Sound device power saving.", "A watt or so at idle." } },
            { "gd_msi_more", new[] { "Line-based interrupts on USB and storage controllers.", "A controller whose driver claims MSI but handles it badly may stop working (Undo fixes it)." } },
            { "gd_wmi_power", new[] { "'Allow the computer to turn off this device' on every device.", "Laptop battery life." } },
            { "gd_wifi_roam", new[] { "Aggressive Wi-Fi roaming.", "Moving between mesh access points is slower." } },
            { "disp_dx_vrr", new string[] { null, "A few games flicker with forced VRR: add them to the exceptions in Graphics settings." } },

            // ---- 3.6.2: Danger zone and experimental (what each one switches off)
            { "exp_serialtimer", new string[] { "Batched timer expiry.", null } },
            { "defender", new string[] { "Microsoft Defender real-time protection: files are no longer scanned as they open.", null } },
            { "sec_firewall", new string[] { "Windows Firewall on every network profile (domain, private, public).", null } },
            { "sec_uac", new string[] { "User Account Control: every program runs with full admin rights without asking.", null } },
            { "smartscreen", new string[] { "SmartScreen reputation checks for downloaded apps and files.", null } },
            { "vbs", new string[] { "Memory Integrity (HVCI) and virtualization-based security.", null } },
            { "sec_spectre", new string[] { "Spectre and Meltdown mitigations in the Windows kernel.", null } },
            { "sec_dep", new string[] { "Data Execution Prevention.", null } },
            { "sec_whql", new string[] { "Driver signature enforcement while Secure Boot is on.", null } },
            { "dz_sac", new string[] { "Smart App Control. Windows can't turn it back on without a reinstall.", null } },
            { "dz_bitlocker", new string[] { "BitLocker / device encryption on the system drive (ArchTweaker decrypts the drive).", null } },
            { "gd_writecache", new string[] { "Write-cache buffer flushing on your disks.", null } },
            { "gd_nvme", new string[] { "Nothing switched off: NVMe drives move from the standard StorNVMe driver to nvmedisk.sys.", null } },
            { "cmd_hypervisor", new string[] { "The Windows hypervisor: Hyper-V, WSL 2, Windows Sandbox, VBS and Memory Integrity.", null } },
            { "pp_idle", new string[] { "CPU idle states (C-states): the CPU never sleeps.", null } },
            { "pp_modernstandby", new string[] { "Modern Standby (S0 low-power idle). Windows uses classic S3 sleep instead.", null } },
            { "net_ipv6off", new string[] { "IPv6 on every network adapter.", null } },
            { "il_keyboard_lockout", new string[] { "The Windows key, Alt+Tab, Alt+Esc, Win-key hotkeys and media keys (hidserv).", null } },
            { "dz_audiooem", new string[] { "OEM audio enhancement services (equalisers, surround and voice effects).", null } },
            { "dz_svc_warning", new string[] { "Mobile hotspot, IP Helper, IP translation, SMB client (Workstation), Program Compatibility Assistant and IPsec policy. Also qWave, Remote Access Connection Manager, smart-card removal policy, UE-V and Windows Hello biometrics.", null } },
            { "dz_nv_rebar", new string[] { "Nothing turned off: ArchTweaker bypasses NVIDIA's per-game Resizable BAR list.", null } },
            { "dz_ntlm_out", new string[] { "Outgoing NTLM sign-in to other machines.", null } },
            { "dz_nv_rmpower", new string[] { "GPU PCIe link power saving (ASPM).", null } },
            { "dz_dev_hpet", new string[] { "The HPET device in Device Manager.", null } },
        };

        internal static IEnumerable<string> EffectIds() { return Effects.Keys; }   // 3.7: for the test that every entry belongs to a tweak

        static void AttachEffects()
        {
            foreach (var t in all)
            {
                string[] e;
                if (!Effects.TryGetValue(t.Id, out e)) continue;
                if (t.Off == null) t.Off = e[0];
                if (t.Breaks == null) t.Breaks = e[1];
            }
        }
    }
}
