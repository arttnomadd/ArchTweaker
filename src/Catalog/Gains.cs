// Expected-gain estimates per tweak. Impact: 0 = no speed effect (privacy / UI / convenience),
// 1 = small (≈0–1 %, sub-ms, mostly consistency), 2 = medium (≈1–5 % or clearly measurable latency / 1 % lows),
// 3 = high (5 %+ on affected systems, or GBs / seconds saved).
// Numbers are typical ranges from public benchmarks and guides; real results depend on hardware, game and what is already set.
using System.Collections.Generic;

namespace ArchTweaker
{
    public static partial class Catalog
    {
        static readonly Dictionary<string, object[]> Gains = new Dictionary<string, object[]>
        {
            // ---------- Power Plan ----------
            { "pp_plan", new object[] { 2, "Average FPS: 0–3 % (often within margin of error in GPU-bound games). 1 % lows: +2–8 % in CPU-bound / high-FPS games. Removes about 1–5 ms clock ramp-up hiccups. Idle power: +10–30 W." } },
            { "pp_minmax", new object[] { 2, "Removes clock ramp-up delay (≈1–10 ms after idle moments). 1 % lows: +1–5 % in CPU-bound titles. Average FPS mostly stays the same. More idle heat." } },
            { "pp_boost", new object[] { 2, "Turbo starts a few ms sooner. +1–3 % in light or bursty CPU loads. 0–1 % in sustained gaming, where the CPU already boosts." } },
            { "pp_epp", new object[] { 3, "The most effective CPU power setting on Intel 8th gen+ and Ryzen 3000+: clocks rise ≈2–10× faster from idle. +2–6 % 1 % lows in CPU-bound games, and a faster desktop. Higher idle power." } },
            { "pp_parking", new object[] { 2, "Removes ≈0.1–1 ms core-unpark latency when a game spawns threads. Fewer micro-stutters; +0–3 % 1 % lows." } },
            { "pp_ramp", new object[] { 1, "Reacts to load every 5 ms instead of 15–30 ms, so clocks are up ≈10–25 ms sooner after a load spike. Little effect on CPUs that use hardware P-states (most modern ones)." } },
            { "pp_latency", new object[] { 1, "On click / key-press hints, clocks and cores go to max instantly. Desktop and menus feel snappier (a few ms). Negligible in-game FPS effect." } },
            { "pp_nothrottle", new object[] { 1, "Prevents rare legacy throttling and duty-cycle dips (mostly laptops). 0–2 %; no effect where the CPU never throttles." } },
            { "pp_hybrid", new object[] { 2, "Intel 12th gen+: game threads go to P-cores first. +2–10 % 1 % lows in games that Thread Director misplaces. 0 % on non-hybrid CPUs." } },
            { "pp_idle", new object[] { 1, "Removes C-state exit latency (≈2–100 µs per wake). DPC latency drops slightly. FPS can go DOWN 1–5 % on modern CPUs, because heat uses up the boost headroom. Benchmark before you keep it." } },
            { "pp_pcie", new object[] { 1, "GPU and NVMe links never pay the ASPM wake penalty (≈2–70 µs). Fewer rare stutters; no average-FPS change." } },
            { "pp_usb", new object[] { 1, "Stops mouse, keyboard and headset dropouts after idle (≈5–50 ms wake on suspend). Consistency fix, not FPS." } },
            { "pp_disk", new object[] { 1, "Avoids the 0.1–5 s spin-up or link-wake stall the first time a game reads an idle drive. No effect while the drive is busy." } },
            { "pp_wifi", new object[] { 2, "Wi-Fi only: removes power-save ping spikes (often 20–200 ms jitter becomes stable ping). Nothing on Ethernet." } },
            { "pp_gpu", new object[] { 1, "Laptops and iGPU/hybrid graphics: up to +5–15 % when the GPU policy was power-saving. Desktops with discrete GPUs: ≈0 %." } },
            { "pp_video", new object[] { 0, "Video playback quality and clocks only. No gaming effect." } },
            { "pp_sleep", new object[] { 0, "Convenience: the PC never sleeps or wakes on timers. No speed effect." } },
            { "pp_unhide", new object[] { 0, "Only makes hidden settings visible in Control Panel. No speed effect by itself." } },
            { "pp_modernstandby", new object[] { 0, "Battery drain and unwanted wake-ups in sleep. No in-use speed effect." } },

            // ---------- Input & Latency ----------
            { "il_timerres", new object[] { 2, "Sleep(1) precision: ≈1–2 ms jitter → ≈0.5 ms. Smoother frame pacing with frame limiters and in older games. Some users report 1–3 % better 1 % lows. Slightly more CPU wake-ups." } },
            { "il_queue", new object[] { 0, "Anecdotal: no measured input-latency change (it's a buffer size, not a polling rate). Placebo for most people." } },
            { "task_diskscan", new object[] { 0, "No FPS change. Removes rare background disk reads that can cause a hitch on a busy drive." } },
            { "task_silentcleanup", new object[] { 0, "No FPS change. Stops a surprise Disk Cleanup run when a drive is nearly full." } },
            { "task_speechmodel", new object[] { 0, "No speed effect. Less background download." } },
            { "task_verifywinre", new object[] { 0, "No speed effect." } },
            { "svc_upnp", new object[] { 0, "No FPS change. Less network broadcast traffic." } },
            { "svc_stisvc", new object[] { 0, "No speed effect. One service less." } },
            { "svc_celltime", new object[] { 0, "No speed effect." } },
            { "il_coalesce0", new object[] { 0, "Unproven: no published frame-time gain. Measure scheduling jitter before you keep it." } },
            { "net_dns_failcache", new object[] { 0, "Names resolve at once after a connection drop. No in-game effect." } },
            { "gd_audio_idle", new object[] { 0, "No frame-time gain. Sound starts at once after silence." } },
            { "net_rss_place", new object[] { 1, "Measured mechanism: network DPCs leave CPU 0. The gain depends on what else runs on CPU 0." } },
            { "il_rawthrottle20", new object[] { 0, "Less CPU use by background apps with a 4000 or 8000 Hz mouse. Focused game: 0 ms." } },
            { "exp_reserved_ecores", new object[] { 1, "Unproven: no published frame-time tests on hybrid CPUs." } },
            { "il_rawthrottle", new object[] { 0, "Only background apps reading the mouse benefit. Focused game: 0 ms. Can slightly INCREASE stutter with 4–8 kHz mice." } },
            { "il_usbpower", new object[] { 1, "Prevents per-device USB suspend (≈5–50 ms re-wake): no missed first click or lost key after idle. Consistency, not FPS." } },

            // ---------- GPU & Devices ----------
            { "gd_nv_drs_power", new object[] { 2, "Fewer clock drops in light or CPU-bound games: steadier frame times, 1 % lows +0–5 %. Higher power while a game runs." } },
            { "gd_nv_drs_latency", new object[] { 2, "GPU-bound DX11 games without Reflex: ≈5–20 ms less input lag. Slightly lower average fps in some titles." } },
            { "gd_nv_drs_shader", new object[] { 1, "Fewer shader-compile stutters after game patches and driver updates. More disk use." } },
            { "gd_nv_drs_frl", new object[] { 2, "On G-Sync/FreeSync: no V-Sync lag spikes at the refresh ceiling, smoother pacing. Costs the top 3 fps." } },
            { "gd_msi", new object[] { 1, "Lower and steadier GPU interrupt/DPC latency (tens of µs). Most modern GPUs already use MSI, so often 0 % change. Mainly fixes stutter on older cards." } },
            { "gd_nv_p0", new object[] { 1, "GPU never downclocks: removes ≈1–3 ms clock ramp-up when FPS spikes or after loading screens. 0–1 % average FPS. +20–60 W idle." } },
            { "gd_nv_hdcp", new object[] { 0, "No proven FPS or latency gain. Breaks protected video." } },
            { "gd_nv_misc", new object[] { 0, "PhysX on GPU helps only old PhysX games (can be +10–50 % there). Everything else is convenience." } },
            { "gd_amd_ulps", new object[] { 1, "Removes wake-up stutter and black-screen issues from AMD deep sleep, mostly in multi-GPU setups. No average-FPS change." } },
            { "gd_pcipower", new object[] { 1, "Fewer device wake delays (µs–ms) on PCI and USB controllers. Small consistency gain." } },
            { "gd_wake", new object[] { 0, "Stops unwanted wake-ups from sleep. No speed effect." } },
            { "gd_nicpower", new object[] { 2, "Ethernet: removes Energy-Efficient Ethernet latency spikes (≈0.1–1 ms per wake, sometimes more on Realtek/Intel). Stops adapter sleep disconnects. Steadier ping." } },
            { "gd_writecache", new object[] { 2, "Write-heavy work (installs, shader compile, unpacking): +5–30 % write speed on SATA drives. Small on NVMe. Risk of data loss on power cut." } },
            { "gd_nvme", new object[] { 3, "Random 4K I/O: +10–80 % IOPS in published tests, with lower CPU per I/O. Real-world: slightly faster boot, load and shader-cache reads. Sequential speed unchanged." } },

            // ---------- Gaming ----------
            { "gamemode", new object[] { 1, "Stops Windows Update and background work during games: fewer sudden drops. 0–2 % average; better worst-case frames on busy systems." } },
            { "gamedvr", new object[] { 2, "Background recording off: +1–5 % FPS on GPUs that were recording, plus less VRAM use and encoder load." } },
            { "gamebar", new object[] { 1, "Removes overlay hooks and the Game Bar process: ≈0–1 %, less RAM, fewer overlay conflicts." } },
            { "hags", new object[] { 1, "Usually 0–2 % FPS (often within margin of error). Can lower latency a little. NECESSARY for DLSS Frame Generation." } },
            { "windowedopt", new object[] { 2, "Borderless/windowed DX10–11 games: ≈1 frame less latency (≈4 ms at 240 Hz) through the flip model, plus Auto HDR and VRR in windowed mode." } },
            { "fso", new object[] { 1, "Older DX9–11 games: exclusive fullscreen can cut ≈0–1 frame of latency. Windows 11 flip model is usually as good, so test it." } },
            { "mmcss", new object[] { 1, "Only helps games and audio that register with MMCSS (few do). 0–2 %, sometimes better audio under load." } },
            { "netthrottle", new object[] { 0, "Tests show it RAISES network DPC latency, with no proven ping gain. Test it yourself." } },
            { "prioritysep", new object[] { 1, "The foreground game gets longer and boosted CPU time slices. 0–3 % in CPU-bound games with background load. The desktop feels faster." } },
            { "powerthrottle", new object[] { 1, "Background apps (Discord, OBS, browser) are no longer forced onto slow cores or low clocks. Fewer hitches when alt-tabbing; laptops lose battery." } },
            { "mouseaccel", new object[] { 0, "No latency change: aim becomes 1:1 and consistent (accuracy, not speed)." } },
            { "mpo", new object[] { 1, "Only fixes flicker or stutter on affected multi-monitor or mixed-refresh setups. Otherwise 0 %." } },
            { "nagle", new object[] { 1, "TCP games only (some MMOs, older titles): up to ≈10–200 ms less delay on small packets. UDP shooters: 0 ms." } },

            // ---------- Responsiveness ----------
            { "startupdelay", new object[] { 2, "Startup apps launch ≈5–10 s sooner after sign-in." } },
            { "hovertime", new object[] { 1, "Tooltips and taskbar previews appear ≈390 ms faster." } },
            { "menudelay", new object[] { 1, "Cascading menus open ≈400 ms faster." } },
            { "keyrepeat", new object[] { 1, "Key repeat starts ≈250 ms sooner. Typing and holding feel faster." } },
            { "folderdisc", new object[] { 2, "Large folders (Downloads, game folders) open 0.5–5 s faster because Explorer skips content sniffing." } },
            { "sepprocess", new object[] { 0, "Stability: a crashing folder window doesn't take down the taskbar. ≈20–50 MB more RAM." } },
            { "hooktimeout", new object[] { 1, "When a hooked app freezes, input stalls ≈1 s at most instead of ≈5 s." } },
            { "svchostsplit", new object[] { 1, "≈40–60 fewer processes and ≈100–300 MB less RAM. Slightly less background scheduling overhead." } },
            { "pagingexec", new object[] { 1, "Kernel never paged out: avoids rare multi-ms stalls under memory pressure. 0 % with plenty of RAM." } },
            { "fth", new object[] { 1, "Stops hidden slow-down shims on apps that crashed before. Can restore a few % in those apps. 0 % in other apps." } },
            { "aeroshake", new object[] { 0, "Convenience only." } },
            { "searchhl", new object[] { 0, "Less clutter and network use in search. No speed effect." } },
            { "ntfsmem", new object[] { 1, "Many-small-file work (game installs, compiles): ≈1–5 % faster metadata operations. 0 % in games." } },
            { "memcompress", new object[] { 1, "32 GB+ RAM: ≈1–3 % less CPU spent compressing memory, fewer micro-hitches. With 16 GB or less it can HURT (more paging)." } },
            { "rs_visualfx", new object[] { 1, "Desktop and window operations feel instant (≈100–250 ms animations removed). Tiny DWM CPU/GPU saving. 0 % in fullscreen games." } },
            { "rs_inputpreload", new object[] { 1, "Two fewer background processes, ≈50–150 MB RAM saved." } },

            // ---------- Privacy ----------
            { "telemetry", new object[] { 1, "Removes DiagTrack's periodic CPU, disk and network bursts (0.5–2 % background CPU spikes). Mostly privacy." } },
            { "adid", new object[] { 0, "Privacy only." } }, { "activity", new object[] { 0, "Privacy only." } },
            { "tailored", new object[] { 0, "Privacy only." } }, { "feedback", new object[] { 0, "No pop-ups. No speed effect." } },
            { "websearch", new object[] { 1, "Start search results appear ≈100–500 ms faster (no web round-trip). Privacy." } },
            { "cortana", new object[] { 0, "Privacy; ≈30–80 MB RAM if Cortana was running." } },
            { "suggestions", new object[] { 0, "No ads or auto-installed apps (saves disk and background installs). No direct FPS effect." } },
            { "trackprogs", new object[] { 0, "Privacy only." } }, { "inking", new object[] { 0, "Privacy only." } },
            { "location", new object[] { 0, "Privacy; one service fewer." } },
            { "wer", new object[] { 0, "No crash-upload bursts. Negligible speed effect." } },
            { "ceip", new object[] { 0, "Privacy; fewer background scans." } },
            { "copilot", new object[] { 0, "≈100–300 MB RAM if Copilot was running. Privacy." } },
            { "recall", new object[] { 2, "Copilot+ PCs: no constant screenshotting and AI indexing (saves CPU/NPU, disk and GBs of storage). 0 on other PCs." } },
            { "chromebg", new object[] { 1, "Chrome stops using RAM and CPU in the background after you close it." } },
            { "bravebg", new object[] { 1, "Brave stops using RAM and CPU in the background after you close it." } },
            { "edgeprelaunch", new object[] { 1, "Saves ≈100–300 MB RAM at boot. Slightly faster sign-in." } },

            // ---------- Interface ----------
            { "darkmode", new object[] { 0, "Appearance only." } },
            { "transparency", new object[] { 1, "Slightly less DWM GPU work (≈0–1 %, mostly iGPUs and laptops)." } },
            { "animations", new object[] { 1, "Windows minimise/restore ≈200 ms faster. Snappier feel." } },
            { "taskleft", new object[] { 0, "Appearance only." } }, { "taskview", new object[] { 0, "Appearance only." } },
            { "widgets", new object[] { 1, "Widgets / WebView host gone: ≈100–400 MB RAM and background network saved." } },
            { "chaticon", new object[] { 0, "Appearance only." } }, { "searchbox", new object[] { 0, "Appearance only." } },
            { "endtask", new object[] { 0, "Convenience: kill frozen apps faster." } },
            { "seconds", new object[] { 0, "Appearance; costs a tiny bit of CPU." } },
            { "lockscreen", new object[] { 1, "Sign-in ≈1 s faster (no lock-screen swipe)." } },
            { "stickykeys", new object[] { 0, "No Sticky Keys pop-up kicking you out of a game." } },
            { "verbose", new object[] { 0, "Diagnostic info only." } }, { "startrec", new object[] { 0, "Privacy and tidiness." } },

            // ---------- Explorer & Context Menu ----------
            { "ext", new object[] { 0, "Safety and clarity only." } }, { "hidden", new object[] { 0, "Visibility only." } },
            { "thispc", new object[] { 1, "Explorer opens slightly faster (no Quick Access / cloud lookups)." } },
            { "classicmenu", new object[] { 1, "Right-click menu one click shorter (≈300–800 ms saved per use)." } },
            { "3dobjects", new object[] { 0, "Tidiness only." } }, { "shortcutsuffix", new object[] { 0, "Tidiness only." } },
            { "takeown", new object[] { 0, "Convenience tool." } }, { "hashmenu", new object[] { 0, "Convenience tool." } },
            { "restartexplorer", new object[] { 0, "Convenience tool." } }, { "killnr", new object[] { 0, "Convenience tool." } },
            { "rebootmenu", new object[] { 0, "Convenience: straight to BIOS for XMP/EXPO changes." } },
            { "noshare", new object[] { 1, "Right-click menu opens a little faster (fewer shell extensions)." } },
            { "nocast", new object[] { 1, "Right-click menu opens a little faster (one less handler)." } },
            { "noprevver", new object[] { 1, "Right-click menu and Properties open a little faster." } },
            { "longpaths", new object[] { 0, "Compatibility only." } },

            // ---------- System ----------
            { "hibernate", new object[] { 3, "Frees disk space equal to ≈40–75 % of your RAM (hiberfil.sys): 6–24 GB on 16–32 GB systems." } },
            { "faststartup", new object[] { 1, "Boot ≈2–8 s slower, but every boot is a clean kernel start (fixes driver and RAM-training oddities)." } },
            { "dop2p", new object[] { 1, "Your upload bandwidth is no longer used to seed updates. Steadier ping during updates." } },
            { "nodrivers", new object[] { 1, "Prevents WU from swapping your tuned GPU or chipset driver (which can cost 5–20 % when it installs an old one)." } },
            { "noautoreboot", new object[] { 0, "Convenience: no forced mid-game restart." } },
            { "wunotify", new object[] { 1, "No surprise background downloads or installs while gaming (those can cost 10–30 % FPS while they run)." } },
            { "lastaccess", new object[] { 1, "≈1–3 % less disk write overhead on file-heavy work." } },
            { "8dot3", new object[] { 1, "Faster file creation in huge folders (≈2–10 % for 100k+ files)." } },
            { "fastshutdown", new object[] { 2, "Shutdown and restart ≈2–8 s faster when apps hang." } },
            { "sysmain", new object[] { 1, "NVMe/SSD: less background disk I/O and ≈50–300 MB RAM. HDD: app launches can get slower." } },
            { "wsearch", new object[] { 1, "No background indexing bursts (CPU/disk); ≈50–200 MB RAM. File-content search becomes slower." } },
            { "reservedstorage", new object[] { 3, "Frees ≈7 GB of disk." } },
            { "storagesense", new object[] { 0, "Prevents unwanted deletions. No speed effect." } },
            { "sys_maintenance", new object[] { 1, "No idle-maintenance jobs still running when you return to a game (they can cost 5–20 % CPU while active)." } },
            { "sys_bsod", new object[] { 0, "Diagnostics only." } }, { "sys_wpbt", new object[] { 1, "No vendor bloat auto-installed by the firmware (saves RAM and background CPU)." } },
            { "sys_pauseupdates", new object[] { 1, "No update downloads, installs or restarts for a year. Security trade-off." } },

            // ---------- Internet & Network ----------
            { "llmnr", new object[] { 0, "Security only." } }, { "smb1", new object[] { 0, "Security only." } },
            { "dnsneg", new object[] { 1, "A site that briefly failed works again instantly instead of after up to 15 min." } },
            { "dnscloudflare", new object[] { 2, "DNS lookups ≈5–50 ms faster than many ISP resolvers (first visit to each site). In-game ping unchanged." } },
            { "doh", new object[] { 0, "Privacy/security; adds ≈0–5 ms on the first lookup." } },
            { "preferipv4", new object[] { 1, "Fixes multi-second connect delays on networks with broken IPv6. 0 ms otherwise." } },
            { "maxconn", new object[] { 1, "Multi-file downloads in WinINet apps up to 2–4× faster to start." } },
            { "wubandwidth", new object[] { 2, "Windows Update can't saturate your line: avoids 50–500 ms ping spikes while it downloads." } },
            { "nicpower", new object[] { 1, "No adapter sleep: avoids reconnect drops and first-packet delays after idle." } },
            { "netbios", new object[] { 0, "Security; slightly less LAN chatter." } }, { "mdnsoff", new object[] { 0, "Less LAN chatter. No speed effect." } },
            { "net_remoteassist", new object[] { 0, "Security only." } },
            { "net_bindings", new object[] { 1, "A few fewer filter drivers on every packet (µs per packet). Mostly theoretical; tidier stack." } },
            { "net_pacer", new object[] { 0, "One filter driver fewer per packet (µs). No measurable change in games." } },
            { "dz_cfg", new object[] { 1, "Removes the CFG check on indirect calls: up to a few % in old games with many indirect calls. Usually within noise." } },
            { "net_ipv6off", new object[] { 0, "No gain on working networks. Can break things." } },

            // ---------- Tasks & Services ----------
            { "tasks_telemetry", new object[] { 1, "Removes periodic background CPU and disk bursts (the Appraiser can use 10–30 % CPU for minutes)." } },
            { "tasks_misc", new object[] { 0, "Fewer small background jobs. Negligible." } },
            { "tasks_xbox", new object[] { 0, "Negligible." } },
            { "svc_misc", new object[] { 1, "≈10–40 MB RAM and a few background wake-ups saved." } },
            { "svc_hyperv", new object[] { 0, "Negligible on physical PCs (they're idle there)." } },
            { "svc_remote", new object[] { 0, "Security; ≈10–30 MB RAM." } }, { "svc_print", new object[] { 0, "≈10–20 MB RAM; security (PrintNightmare class)." } },
            { "svc_xbox", new object[] { 0, "≈10–30 MB RAM." } }, { "svc_bt", new object[] { 0, "≈5–15 MB RAM." } },
            { "svc_diag", new object[] { 1, "Fewer diagnostic background scans; ≈20–60 MB RAM." } },
            { "svc_touch", new object[] { 0, "≈10–30 MB RAM." } }, { "svc_cdp", new object[] { 0, "≈10–40 MB RAM and less background network chatter." } },

            // ---------- Command tweaks ----------
            { "cmd_dynamictick", new object[] { 1, "Steadier timer ticks. Some users see ≈0.1–0.5 ms lower DPC jitter and smoother frame pacing. More idle power." } },
            { "cmd_bootmenu", new object[] { 0, "Convenience (F8 Safe Mode). Boot ≈1 s slower." } },
            { "cmd_hypervisor", new object[] { 2, "Removes hypervisor overhead: ≈2–8 % in some games, lower DPC latency. Also turns off VBS / Memory Integrity." } },
            { "cmd_trim", new object[] { 2, "Stops SSD write speed from degrading 10–50 % over months (if an old tweak disabled TRIM). 0 % if TRIM was already on." } },
            { "cmd_autotuning", new object[] { 3, "If auto-tuning was disabled: downloads can be 2–10× faster on fast or high-latency links. 0 % if it was already normal." } },
            { "cmd_rss", new object[] { 1, "Multi-gigabit or busy networks: spreads load across cores (lower CPU per packet). Little at 1 Gbps." } },
            { "cmd_heuristics", new object[] { 1, "Prevents Windows from shrinking the TCP window on 'bad' networks (can recover throughput there)." } },
            { "cmd_fastopen", new object[] { 1, "Saves one round-trip (≈10–100 ms) on reconnects to supporting servers. Most games: 0 ms." } },
            { "cmd_teredo", new object[] { 0, "Less background tunnelling. No ping change for most games." } },
            { "cmd_dupplans", new object[] { 0, "Tidiness only." } },

            // ---------- Danger zone (was Security (advanced)) ----------
            { "uacdim", new object[] { 1, "UAC prompts appear ≈0.5–1 s faster (no desktop switch)." } },
            { "smartscreen", new object[] { 1, "Unknown downloads open ≈0.5–2 s faster (no reputation check)." } },
            { "defender", new object[] { 3, "Real-time scanning off: file-heavy work (installs, compiles, game file checks, shader caches) is ≈10–40 % faster. Games: 0–3 %." } },
            { "vbs", new object[] { 3, "Memory Integrity / VBS off: ≈5 % average FPS, up to ≈8–10 % in some games (Tom's Hardware, ComputerBase tests)." } },
            { "sec_spectre", new object[] { 2, "Older Intel (≤9th gen): 5–20 % in syscall/IO-heavy work, 0–5 % in games. Ryzen and new Intel: usually within noise." } },
        };

        static void AttachGains()
        {
            foreach (var t in all)
            {
                object[] g;
                if (Gains.TryGetValue(t.Id, out g) || Gains15.TryGetValue(t.Id, out g) || GainsU.TryGetValue(t.Id, out g) || GainsR.TryGetValue(t.Id, out g) || GainsD.TryGetValue(t.Id, out g) || GainsV34.TryGetValue(t.Id, out g) || GainsV35.TryGetValue(t.Id, out g) || GainsV36.TryGetValue(t.Id, out g) || GainsV39.TryGetValue(t.Id, out g)) { t.Impact = (int)g[0]; t.Gain = (string)g[1]; }
                else { t.Impact = 0; t.Gain = "No measurable speed effect expected."; }
            }
        }
    }
}
