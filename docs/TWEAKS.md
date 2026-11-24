# ArchTweaker tweak list

Generated from the catalog of ArchTweaker 3.43.0 by `tools/DumpTweaks.cs`. Do not edit by hand.

287 tweaks. **Lite** = shown in Lite mode (safe, not experimental). **Pro** = shown only in Pro mode.
Impact: **HIGH** = 5 %+ / GBs / seconds · **MEDIUM** = 1–5 % or clearly measurable latency · **LOW** = under 1 %, consistency or ms-level feel · **NONE** = privacy, appearance or convenience.
The ranges are typical public-test results, not guarantees. The details pane in the app shows the same text, plus what each tweak turns off and can break.

## Privacy (38)

| Tweak | Mode | Risk | Impact | Expected gain |
|---|---|---|---|---|
| Minimize telemetry | Lite | Safe | LOW | Removes DiagTrack's periodic CPU, disk and network bursts (0.5–2 % background CPU spikes). Mostly privacy. |
| Disable advertising ID | Lite | Safe | NONE | Privacy only. |
| Disable activity history / Timeline | Lite | Safe | NONE | Privacy only. |
| Disable tailored experiences | Lite | Safe | NONE | Privacy only. |
| Never ask for feedback | Lite | Safe | NONE | No pop-ups. No speed effect. |
| Disable Bing / web results in Start search | Lite | Safe | LOW | Start search results appear ≈100–500 ms faster (no web round-trip). Privacy. |
| Disable Cortana | Lite | Safe | NONE | Privacy; ≈30–80 MB RAM if Cortana was running. |
| Disable ads, suggestions and silent app installs | Lite | Safe | NONE | No ads or auto-installed apps (saves disk and background installs). No direct FPS effect. |
| Don't track app launches | Lite | Safe | NONE | Privacy only. |
| Disable inking & typing personalization | Lite | Safe | NONE | Privacy only. |
| Disable location access | Lite | Safe | NONE | Privacy; one service fewer. |
| Disable Windows Error Reporting | Lite | Safe | NONE | No crash-upload bursts. Negligible speed effect. |
| Disable Customer Experience Improvement Program | Lite | Safe | NONE | Privacy; fewer background scans. |
| Disable Copilot | Lite | Safe | NONE | ≈100–300 MB RAM if Copilot was running. Privacy. |
| Disable Recall / AI snapshots | Lite | Safe | MEDIUM | Copilot+ PCs: no constant screenshotting and AI indexing (saves CPU/NPU, disk and GBs of storage). 0 on other PCs. |
| Stop Edge pre-launch & background running | Lite | Safe | LOW | Saves ≈100–300 MB RAM at boot. Slightly faster sign-in. |
| Stop Chrome running in the background | Lite | Safe | LOW | Chrome stops using RAM and CPU in the background after you close it. |
| Stop Brave running in the background | Lite | Safe | LOW | Brave stops using RAM and CPU in the background after you close it. |
| Stop UWP apps running in the background | Lite | Safe | LOW | Less background CPU, disk and RAM use from UWP apps. Fewer background-task stutters on busy systems. No average-FPS change. |
| Don't keep a recent-documents history | Lite | Safe | NONE | No speed effect. Small privacy gain: no recent-files history kept. |
| Remove web/Store lookup from "Open with" | Lite | Safe | NONE | No speed effect. Keeps unknown file types from sending you to the Store or web. |
| Search: no cloud content from your accounts | Lite | Safe | NONE | Privacy; local search results appear a little sooner. |
| Paint: no AI features (Cocreator, Image Creator, generative fill) | Lite | Safe | NONE | Privacy only. |
| Lock screen: no Spotlight, tips or fun facts | Lite | Safe | NONE | Privacy; no background image downloads. |
| No notifications on the lock screen | Lite | Safe | NONE | Privacy only. |
| Don't download device info and icons | Lite | Safe | NONE | Privacy; less background traffic when devices connect. |
| Turn off Windows Ink Workspace | Lite | Safe | NONE | Tidiness only. |
| Phone Link and cross-device features off | Lite | Safe | NONE | Privacy; less background sync. |
| No account and backup nags | Lite | Safe | NONE | Fewer nags. No speed effect. |
| No tips, setup reminders or typing insights | Lite | Safe | NONE | Privacy and fewer pop-ups. No speed effect. |
| Websites can't read your language list | Lite | Safe | NONE | Privacy only. |
| PowerShell telemetry off | Lite | Safe | NONE | Privacy only. |
| Store apps can't read contacts, calendar, email or calls | Lite | Safe | NONE | Privacy only. |
| Store apps can't read your files | Lite | Safe | NONE | Privacy only. |
| Store apps can't read diagnostics, radios or capture without a border | Lite | Safe | NONE | Privacy only. |
| Turn off background trace sessions (autologgers) | Lite | Safe | LOW | Less background disk writing and a few DPCs less. Mostly privacy. |
| Turn off biometrics (policy) | Pro | Moderate | NONE | Privacy only: Windows stops using fingerprint and face data. |
| Disable Windows Error Reporting (policy) | Lite | Safe | NONE | No crash-upload bursts. Negligible speed effect. |

## Interface (22)

| Tweak | Mode | Risk | Impact | Expected gain |
|---|---|---|---|---|
| Dark mode (apps + system) | Lite | Safe | NONE | Appearance only. |
| Disable transparency effects | Lite | Safe | LOW | Slightly less DWM GPU work (≈0–1 %, mostly iGPUs and laptops). |
| Faster UI: reduce animations | Lite | Safe | LOW | Windows minimise/restore ≈200 ms faster. Snappier feel. |
| Align taskbar to the left | Lite | Safe | NONE | Appearance only. |
| Hide Task View button | Lite | Safe | NONE | Appearance only. |
| Disable Widgets / News and Interests | Lite | Safe | LOW | Widgets / WebView host gone: ≈100–400 MB RAM and background network saved. |
| Hide Chat (Teams) icon | Lite | Safe | NONE | Appearance only. |
| Hide taskbar search box | Lite | Safe | NONE | Appearance only. |
| Add 'End task' to taskbar right-click | Lite | Safe | NONE | Convenience: kill frozen apps faster. |
| Show seconds in taskbar clock | Lite | Safe | NONE | Appearance; costs a tiny bit of CPU. |
| Skip the lock screen | Lite | Safe | LOW | Sign-in ≈1 s faster (no lock-screen swipe). |
| Disable Sticky Keys shortcut (Shift ×5) | Lite | Safe | NONE | No Sticky Keys pop-up kicking you out of a game. |
| Verbose sign-in / shutdown status | Lite | Safe | NONE | Diagnostic info only. |
| Start & Quick Access: no recent files | Lite | Safe | NONE | Privacy and tidiness. |
| Standard font smoothing instead of ClearType | Lite | Safe | NONE | No speed effect. Cleaner text on panels where ClearType shows colour fringes. |
| Fix blurry apps on scaled displays | Lite | Safe | NONE | No speed effect. Sharper old apps after changing scaling or docking. |
| Full-quality wallpaper (no JPEG recompression) | Lite | Safe | NONE | No speed effect. Removes JPEG banding on the desktop background. |
| Snap: no layout pop-ups | Lite | Safe | NONE | Fewer pop-ups while moving windows. No speed effect. |
| Start: hide the Recommended section | Lite | Safe | NONE | A cleaner Start menu. No speed effect. |
| Taskbar: hide the Copilot button | Lite | Safe | NONE | Tidiness only. |
| Dynamic Lighting off | Lite | Safe | NONE | Stops RGB apps fighting with Windows. No speed effect. |
| No Alt+Shift / Ctrl+Shift layout switching | Lite | Safe | NONE | No accidental layout switches in games. No speed effect. |

## Explorer & Context Menu (18)

| Tweak | Mode | Risk | Impact | Expected gain |
|---|---|---|---|---|
| Show file extensions | Lite | Safe | NONE | Safety and clarity only. |
| Show hidden files | Lite | Safe | NONE | Visibility only. |
| Open Explorer to 'This PC' | Lite | Safe | LOW | Explorer opens slightly faster (no Quick Access / cloud lookups). |
| Classic right-click menu (no 'Show more options') | Lite | Safe | LOW | Right-click menu one click shorter (≈300–800 ms saved per use). |
| Remove '3D Objects' from This PC | Lite | Safe | NONE | Tidiness only. |
| No '- Shortcut' suffix on new shortcuts | Lite | Safe | NONE | Tidiness only. |
| Add 'Take ownership' to right-click menu | Lite | Safe | NONE | Convenience tool. |
| Add 'SHA-256 hash' to right-click menu | Lite | Safe | NONE | Convenience tool. |
| Add 'Restart Explorer' to desktop right-click | Lite | Safe | NONE | Convenience tool. |
| Add 'Kill not-responding apps' to desktop right-click | Lite | Safe | NONE | Convenience tool. |
| Add 'Reboot to BIOS/UEFI' to desktop right-click | Lite | Safe | NONE | Convenience: straight to BIOS for XMP/EXPO changes. |
| Remove 'Share' and 'Give access to' from menus | Lite | Safe | LOW | Right-click menu opens a little faster (fewer shell extensions). |
| Remove 'Cast to Device' from menus | Lite | Safe | LOW | Right-click menu opens a little faster (one less handler). |
| Remove 'Restore previous versions' from menus | Lite | Safe | LOW | Right-click menu and Properties open a little faster. |
| Enable long file paths (>260 chars) | Lite | Safe | NONE | Compatibility only. |
| Show icons instead of thumbnails | Lite | Safe | LOW | Large image and video folders open much faster. No thumbnail generation on disk. |
| Larger icon cache (4096 icons) | Lite | Safe | NONE | No FPS effect. Fewer blank/redrawn icons in Explorer and the Start menu. |
| No thumbs.db on network folders | Lite | Safe | NONE | No speed effect. No file locks or thumbs.db clutter on network shares. |

## System (21)

| Tweak | Mode | Risk | Impact | Expected gain |
|---|---|---|---|---|
| Disable hibernation | Lite | Safe | HIGH | Frees disk space equal to ≈40–75 % of your RAM (hiberfil.sys): 6–24 GB on 16–32 GB systems. |
| Disable Fast Startup | Lite | Safe | LOW | Boot ≈2–8 s slower, but every boot is a clean kernel start (fixes driver and RAM-training oddities). |
| Disable Delivery Optimization P2P upload | Lite | Safe | LOW | Your upload bandwidth is no longer used to seed updates. Steadier ping during updates. |
| Don't install drivers through Windows Update | Lite | Safe | LOW | Prevents WU from swapping your tuned GPU or chipset driver (which can cost 5–20 % when it installs an old one). |
| No forced restarts for updates while signed in | Lite | Safe | NONE | Convenience: no forced mid-game restart. |
| Windows Update: notify before download | Pro | Moderate | LOW | No surprise background downloads or installs while gaming (those can cost 10–30 % FPS while they run). |
| Disable NTFS last-access timestamps | Lite | Safe | LOW | ≈1–3 % less disk write overhead on file-heavy work. |
| Disable 8.3 short-name creation | Lite | Safe | LOW | Faster file creation in huge folders (≈2–10 % for 100k+ files). |
| Faster shutdown | Lite | Safe | MEDIUM | Shutdown and restart ≈2–8 s faster when apps hang. |
| Disable SysMain (Superfetch) | Pro | Moderate | LOW | NVMe/SSD: less background disk I/O and ≈50–300 MB RAM. HDD: app launches can get slower. |
| Disable Windows Search indexing | Pro | Moderate | LOW | No background indexing bursts (CPU/disk); ≈50–200 MB RAM. File-content search becomes slower. |
| Disable Reserved Storage | Lite | Safe | HIGH | Frees ≈7 GB of disk. |
| Disable Storage Sense auto-cleanup | Lite | Safe | NONE | Prevents unwanted deletions. No speed effect. |
| Disable automatic maintenance | Pro | Moderate | LOW | No idle-maintenance jobs still running when you return to a game (they can cost 5–20 % CPU while active). |
| Show technical details on blue screens | Lite | Safe | NONE | Diagnostics only. |
| Pause Windows Update for 1 year | Pro | Moderate | LOW | No update downloads, installs or restarts for a year. Security trade-off. |
| Don't archive unused apps | Lite | Safe | NONE | Apps start right away instead of re-downloading first. No speed effect otherwise. |
| Store: no automatic app updates | Pro | Moderate | LOW | No Store downloads eating bandwidth or disk while you play. Nothing when nothing is updating. |
| Stay on this Windows version | Pro | Moderate | NONE | No surprise feature upgrade (which resets many tweaks). No speed effect. |
| Turn off Sleep Study | Lite | Safe | NONE | No Sleep Study logging in the background. Negligible speed effect. |
| UAC: don't dim the desktop | Pro | Moderate | LOW | UAC prompts appear ≈0.5–1 s faster (no desktop switch). |

## Responsiveness (22)

| Tweak | Mode | Risk | Impact | Expected gain |
|---|---|---|---|---|
| Remove startup-apps delay | Lite | Safe | MEDIUM | Startup apps launch ≈5–10 s sooner after sign-in. |
| Faster hover tooltips & taskbar previews | Lite | Safe | LOW | Tooltips and taskbar previews appear ≈390 ms faster. |
| Instant menus (MenuShowDelay 0) | Lite | Safe | LOW | Cascading menus open ≈400 ms faster. |
| Fastest keyboard repeat | Lite | Safe | LOW | Key repeat starts ≈250 ms sooner. Typing and holding feel faster. |
| Disable automatic folder-type discovery | Lite | Safe | MEDIUM | Large folders (Downloads, game folders) open 0.5–5 s faster because Explorer skips content sniffing. |
| Open folder windows in a separate process | Lite | Safe | NONE | Stability: a crashing folder window doesn't take down the taskbar. ≈20–50 MB more RAM. |
| Shorter low-level hook timeout | Lite | Safe | LOW | When a hooked app freezes, input stalls ≈1 s at most instead of ≈5 s. |
| Group svchost services (fewer processes) | Pro | Moderate | LOW | ≈40–60 fewer processes and ≈100–300 MB less RAM. Slightly less background scheduling overhead. |
| Keep kernel & drivers in RAM | Pro | Moderate | LOW | Kernel never paged out: avoids rare multi-ms stalls under memory pressure. 0 % with plenty of RAM. |
| Disable Fault Tolerant Heap | Pro | Moderate | LOW | Stops hidden slow-down shims on apps that crashed before. Can restore a few % in those apps. 0 % in other apps. |
| Disable Aero Shake | Lite | Safe | NONE | Convenience only. |
| Disable search highlights | Lite | Safe | NONE | Less clutter and network use in search. No speed effect. |
| Larger NTFS memory cache (fsutil memoryusage 2) | Pro | Moderate | LOW | Many-small-file work (game installs, compiles): ≈1–5 % faster metadata operations. 0 % in games. |
| Disable memory compression | Pro | Moderate | LOW | 32 GB+ RAM: ≈1–3 % less CPU spent compressing memory, fewer micro-hitches. With 16 GB or less it can HURT (more paging). |
| Visual effects: best performance (keeps smooth fonts) | Lite | Safe | LOW | Desktop and window operations feel instant (≈100–250 ms animations removed). Tiny DWM CPU/GPU saving. 0 % in fullscreen games. |
| Don't preload input experience & widgets | Lite | Safe | LOW | Two fewer background processes, ≈50–150 MB RAM saved. |
| Disable prefetch & app pre-launch (SuperFetch features) | Pro | Moderate | LOW | Stops prefetch/pre-launch background disk and CPU work and frees a little RAM. You notice it mostly on HDDs. Near zero on NVMe like yours. |
| Stop the "low disk space" pop-up | Lite | Safe | NONE | No speed effect — removes the low-disk-space nag only. |
| Stop broken-shortcut network search | Lite | Safe | LOW | Removes multi-second Explorer hangs when a shortcut's target has moved. No FPS effect. |
| Enlarge the client DNS cache | Pro | Moderate | LOW | Slightly snappier repeat web lookups (served from the local cache for longer). No game-FPS effect. |
| Re-enable automatic registry backups | Lite | Safe | NONE | No speed effect — a recovery/safety-net feature. Uses a little extra disk. |
| Kernel: don't track IO priority boosts | Pro | Moderate | LOW | Unproven: less kernel bookkeeping when a thread waits on disk. Often within noise. |

## Gaming (15)

| Tweak | Mode | Risk | Impact | Expected gain |
|---|---|---|---|---|
| Enable Game Mode | Lite | Safe | LOW | Stops Windows Update and background work during games: fewer sudden drops. 0–2 % average; better worst-case frames on busy systems. |
| Disable Game DVR background recording | Lite | Safe | MEDIUM | Background recording off: +1–5 % FPS on GPUs that were recording, plus less VRAM use and encoder load. |
| Disable Xbox Game Bar overlay | Pro | Moderate | LOW | Removes overlay hooks and the Game Bar process: ≈0–1 %, less RAM, fewer overlay conflicts. |
| Enable Hardware-accelerated GPU scheduling (HAGS) | Lite | Safe | LOW | Usually 0–2 % FPS (often within margin of error). Can lower latency a little. NECESSARY for DLSS Frame Generation. |
| Optimizations for windowed games | Lite | Safe | MEDIUM | Borderless/windowed DX10–11 games: ≈1 frame less latency (≈4 ms at 240 Hz) through the flip model, plus Auto HDR and VRR in windowed mode. |
| Disable fullscreen optimizations globally | Pro | Moderate | LOW | Older DX9–11 games: exclusive fullscreen can cut ≈0–1 frame of latency. Windows 11 flip model is usually as good, so test it. |
| MMCSS: prioritise games | Lite | Safe | LOW | Only helps games and audio that register with MMCSS (few do). 0–2 %, sometimes better audio under load. |
| Disable network throttling | Pro | Moderate | NONE | Tests show it RAISES network DPC latency, with no proven ping gain. Test it yourself. |
| Foreground boost (Win32PrioritySeparation 0x26) | Lite | Safe | LOW | The foreground game gets longer and boosted CPU time slices. 0–3 % in CPU-bound games with background load. The desktop feels faster. |
| Disable Power Throttling | Lite | Safe | LOW | Background apps (Discord, OBS, browser) are no longer forced onto slow cores or low clocks. Fewer hitches when alt-tabbing; laptops lose battery. |
| Disable mouse acceleration | Lite | Safe | NONE | No latency change: aim becomes 1:1 and consistent (accuracy, not speed). |
| Disable Multiplane Overlay (MPO) | Pro | Moderate | LOW | Only fixes flicker or stutter on affected multi-monitor or mixed-refresh setups. Otherwise 0 %. |
| Disable Nagle's algorithm (TCP no-delay) | Pro | Moderate | LOW | TCP games only (some MMOs, older titles): up to ≈10–200 ms less delay on small packets. UDP shooters: 0 ms. |
| MMCSS: no lazy mode, games marked latency-sensitive | Pro | Moderate | LOW | Slightly steadier scheduling for games and audio while MMCSS is active. Usually within noise. |
| Game Bar removed: stop the 'ms-gamebar' pop-up | Lite | Safe | NONE | Removes a pop-up. No speed effect. |

## Power Plan (22)

| Tweak | Mode | Risk | Impact | Expected gain |
|---|---|---|---|---|
| Create & activate 'ArchTweaker Max Performance' plan | Lite | Safe | MEDIUM | Average FPS: 0–3 % (often within margin of error in GPU-bound games). 1 % lows: +2–8 % in CPU-bound / high-FPS games. Removes about 1–5 ms clock ramp-up hiccups. Idle power: +10–30 W. |
| CPU minimum & maximum state 100% | Lite | Safe | MEDIUM | Removes clock ramp-up delay (≈1–10 ms after idle moments). 1 % lows: +1–5 % in CPU-bound titles. Average FPS mostly stays the same. More idle heat. |
| Processor boost mode: Aggressive | Lite | Safe | MEDIUM | Turbo starts a few ms sooner. +1–3 % in light or bursty CPU loads. 0–1 % in sustained gaming, where the CPU already boosts. |
| Energy Performance Preference = 0 (maximum performance) | Lite | Safe | HIGH | The most effective CPU power setting on Intel 8th gen+ and Ryzen 3000+: clocks rise ≈2–10× faster from idle. +2–6 % 1 % lows in CPU-bound games, and a faster desktop. Higher idle power. |
| Disable CPU core parking | Lite | Safe | MEDIUM | Removes ≈0.1–1 ms core-unpark latency when a game spawns threads. Fewer micro-stutters; +0–3 % 1 % lows. |
| Instant clock ramp-up (Rocket policy) | Lite | Safe | LOW | Reacts to load every 5 ms instead of 15–30 ms, so clocks are up ≈10–25 ms sooner after a load spike. Little effect on CPUs that use hardware P-states (most modern ones). |
| Latency-sensitivity hints → 100% | Lite | Safe | LOW | On click / key-press hints, clocks and cores go to max instantly. Desktop and menus feel snappier (a few ms). Negligible in-game FPS effect. |
| No throttle states, no duty cycling, active cooling | Lite | Safe | LOW | Prevents rare legacy throttling and duty-cycle dips (mostly laptops). 0–2 %; no effect where the CPU never throttles. |
| Hybrid CPUs: prefer P-cores for all threads | Pro | Moderate | MEDIUM | Intel 12th gen+: game threads go to P-cores first. +2–10 % 1 % lows in games that Thread Director misplaces. 0 % on non-hybrid CPUs. |
| PCIe Link State Power Management off | Lite | Safe | LOW | GPU and NVMe links never pay the ASPM wake penalty (≈2–70 µs). Fewer rare stutters; no average-FPS change. |
| USB: no selective suspend, no USB3 link power saving | Lite | Safe | LOW | Stops mouse, keyboard and headset dropouts after idle (≈5–50 ms wake on suspend). Consistency fix, not FPS. |
| Disks never power down (HDD, SATA link, NVMe idle) | Lite | Safe | LOW | Avoids the 0.1–5 s spin-up or link-wake stall the first time a game reads an idle drive. No effect while the drive is busy. |
| Wi-Fi adapter: maximum performance | Lite | Safe | MEDIUM | Wi-Fi only: removes power-save ping spikes (often 20–200 ms jitter becomes stable ping). Nothing on Ethernet. |
| GPU power settings: maximum performance | Lite | Safe | LOW | Laptops and iGPU/hybrid graphics: up to +5–15 % when the GPU policy was power-saving. Desktops with discrete GPUs: ≈0 %. |
| Video playback: performance bias | Lite | Safe | NONE | Video playback quality and clocks only. No gaming effect. |
| Never sleep / hibernate, no hybrid sleep, no wake timers | Pro | Moderate | NONE | Convenience: the PC never sleeps or wakes on timers. No speed effect. |
| Show all hidden power settings in Control Panel | Lite | Safe | NONE | Only makes hidden settings visible in Control Panel. No speed effect by itself. |
| NVMe: no idle power states (APST latency tolerance 0) | Lite | Safe | LOW | Removes the NVMe wake-up from deeper power states (≈1–25 ms for the deepest ones, drive-dependent). Fewer first-access stalls when streaming assets. No effect on throughput. |
| No away mode, no networking in standby | Pro | Moderate | NONE | Stops wake-ups and background sync while the PC is in standby. No in-use speed effect. |
| Display never dims; no adaptive brightness | Lite | Safe | NONE | Convenience: the screen doesn't dim. No speed effect. |
| No deep idle sleep (idle resiliency) | Pro | Moderate | LOW | Modern Standby PCs only: removes some wake-up delay after idle moments. Nothing on desktops without the setting. |
| Power: lowest latency tolerance for devices and graphics *(experimental)* | Pro | Moderate | LOW | Unproven: can trim device wake-up delays. Often within noise. |

## Input & Latency (10)

| Tweak | Mode | Risk | Impact | Expected gain |
|---|---|---|---|---|
| Timer resolution 0.5 ms (background service) | Lite | Safe | MEDIUM | Sleep(1) precision: ≈1–2 ms jitter → ≈0.5 ms. Smoother frame pacing with frame limiters and in older games. Some users report 1–3 % better 1 % lows. Slightly more CPU wake-ups. |
| Smaller mouse & keyboard input buffers | Pro | Moderate | NONE | Anecdotal: no measured input-latency change (it's a buffer size, not a polling rate). Placebo for most people. |
| Remove background raw-mouse throttle (Windows 11) | Pro | Moderate | NONE | Only background apps reading the mouse benefit. Focused game: 0 ms. Can slightly INCREASE stutter with 4–8 kHz mice. |
| Kernel: spread timer expiry across cores | Pro | Moderate | LOW | Can move timer DPCs off core 0: smaller DPC spikes where core 0 is busy with device interrupts. Often no measurable change. |
| Mouse acceleration off on the sign-in screen and for new users | Lite | Safe | NONE | Same pointer feel on the sign-in screen. No in-game effect. |
| Kernel: serialize timer expiration *(experimental)* | Pro | Moderate | LOW | Unproven: reports range from smoother frame times to no change. |
| Kernel: interrupt steering mode 2 *(experimental)* | Pro | Advanced | LOW | Unproven: can move interrupt work off busy cores, or onto the core the game uses. |
| Background raw-mouse rate: 50 Hz (Windows 11) | Lite | Safe | NONE | Less CPU use by background apps with a 4000 or 8000 Hz mouse. Focused game: 0 ms. |
| Kernel: reserve the E-cores (ReservedCpuSets) *(experimental)* | Pro | Advanced | LOW | Unproven: no published frame-time tests on hybrid CPUs. |
| Timer coalescing interval: 0 *(experimental)* | Pro | Moderate | NONE | Unproven: no published frame-time gain. Measure scheduling jitter before you keep it. |

## GPU & Devices (34)

| Tweak | Mode | Risk | Impact | Expected gain |
|---|---|---|---|---|
| GPU: Message-Signaled Interrupts (MSI mode) | Pro | Moderate | LOW | Lower and steadier GPU interrupt/DPC latency (tens of µs). Most modern GPUs already use MSI, so often 0 % change. Mainly fixes stutter on older cards. |
| NVIDIA: lock highest performance state (P0) | Pro | Advanced | LOW | GPU never downclocks: removes ≈1–3 ms clock ramp-up when FPS spikes or after loading screens. 0–1 % average FPS. +20–60 W idle. |
| NVIDIA: disable HDCP | Pro | Moderate | NONE | No proven FPS or latency gain. Breaks protected video. |
| NVIDIA: PhysX on GPU, no tray icon, developer settings | Lite | Safe | NONE | PhysX on GPU helps only old PhysX games (can be +10–50 % there). Everything else is convenience. |
| NVIDIA: turn off telemetry and update checks | Lite | Safe | NONE | No FPS effect. A few fewer background tasks and no telemetry uploads. Driver-update checks no longer run daily. |
| NVIDIA: power management mode = Prefer maximum performance | Lite | Safe | MEDIUM | Fewer clock drops in light or CPU-bound games: steadier frame times, 1 % lows +0–5 %. Higher power while a game runs. |
| NVIDIA: low latency mode = On | Lite | Safe | MEDIUM | GPU-bound DX11 games without Reflex: ≈5–20 ms less input lag. Slightly lower average fps in some titles. |
| NVIDIA: shader cache size = Unlimited | Lite | Safe | LOW | Fewer shader-compile stutters after game patches and driver updates. More disk use. |
| NVIDIA: cap frame rate 3 below refresh (G-Sync / VRR) | Pro | Moderate | MEDIUM | On G-Sync/FreeSync: no V-Sync lag spikes at the refresh ceiling, smoother pacing. Costs the top 3 fps. |
| AMD: disable ULPS (Ultra Low Power State) | Pro | Moderate | LOW | Removes wake-up stutter and black-screen issues from AMD deep sleep, mostly in multi-GPU setups. No average-FPS change. |
| Devices: no power saving (PCI, ACPI and 'allow turn off') | Pro | Moderate | LOW | Fewer device wake delays (µs–ms) on PCI and USB controllers. Small consistency gain. |
| Devices can't wake the PC | Lite | Safe | NONE | Stops unwanted wake-ups from sleep. No speed effect. |
| Network adapters: no power saving, no Green Ethernet, no wake | Lite | Safe | MEDIUM | Ethernet: removes Energy-Efficient Ethernet latency spikes (≈0.1–1 ms per wake, sometimes more on Realtek/Intel). Stops adapter sleep disconnects. Steadier ping. |
| Wi-Fi: roam only on very weak signal (Intel / Killer) | Lite | Safe | MEDIUM | Wi-Fi only: removes periodic roaming-scan ping spikes (typically 20–200 ms every 30–120 s on Intel AX2xx). Average ping unchanged. Nothing on Ethernet. |
| Hardware composed: force immediate flip | Pro | Moderate | LOW | Flip-model fullscreen games: can shave roughly 1 frame of present latency on some setups, nothing on others. Measure with PresentMon. Can cause flicker — test. |
| Intel iGPU: async flip + low-latency mode | Pro | Moderate | LOW | Intel integrated graphics only: lower flip latency on the iGPU. Zero effect when your discrete GPU drives the display. |
| Variable refresh rate for all games | Lite | Safe | LOW | Smoother frame pacing (no tearing, less stutter) in games without native VRR support. Needs a VRR monitor. |
| Auto HDR for DirectX 11/12 games | Lite | Safe | NONE | Image quality, not speed — a small GPU cost. Only works with HDR on. |
| Audio devices: no power saving | Pro | Moderate | NONE | Removes the pop or delay when audio starts after silence. No speed effect. |
| MSI mode for USB and storage controllers | Pro | Moderate | LOW | Fewer shared interrupts for mouse, headset and SSD: slightly steadier DPC latency. Often already MSI by default. |
| NVIDIA: texture filtering for speed | Pro | Moderate | LOW | ≈1–4 % FPS in texture-heavy games. Less in modern titles that set their own filtering. |
| NVIDIA: no Ansel, FXAA, Smooth Motion or CUDA P2 clocks | Lite | Safe | LOW | Full memory clocks for CUDA apps and no driver overlays. Usually no FPS change in games. |
| NVIDIA: V-Sync and G-SYNC forced off (competitive) | Pro | Moderate | MEDIUM | Removes V-Sync queueing (≈10–50 ms) where a game forced it on. Tearing appears; no G-SYNC. |
| USB & HID: no power saving (drivers and each device) | Lite | Safe | LOW | No USB / HID wake-up stalls (≈5–50 ms) after idle. Consistency fix, not FPS. |
| Disable unused virtual system devices | Pro | Moderate | NONE | A few drivers and interrupt sources less. Usually not measurable. |
| Disable NVIDIA Virtual Audio Device | Pro | Moderate | NONE | One audio endpoint less. No speed effect. |
| Laptop panel: no self-refresh, dynamic refresh or display power saving | Pro | Moderate | MEDIUM | Laptops: removes refresh-rate drops and PSR stutter, for steady frame pacing on the built-in screen. |
| NVIDIA: always use the latest DLSS model | Pro | Moderate | NONE | Better image quality. ≈2–5 % less FPS than the old DLSS model at the same preset. |
| NVIDIA: show the DLSS indicator in games | Lite | Safe | NONE | Diagnostic overlay only. |
| AMD driver: Anti-Lag on, no clock deep sleep, no memory stutter *(experimental)* | Pro | Moderate | LOW | Unproven: steadier GPU clocks between frames. Anti-Lag lowers latency when the GPU is the limit. |
| Graphics: no VSync idle timeout *(experimental)* | Pro | Moderate | LOW | Unproven: reports range from steadier frame pacing to no change. |
| Audio devices: no idle power-down | Lite | Safe | NONE | No frame-time gain. Sound starts at once after silence. |
| Audio enhancements off | Lite | Safe | NONE | No measurable speed effect expected. |
| Audio threads get high scheduling priority | Lite | Safe | NONE | No measurable speed effect expected. |

## Internet & Network (15)

| Tweak | Mode | Risk | Impact | Expected gain |
|---|---|---|---|---|
| Disable active connectivity probes (NCSI) | Pro | Moderate | NONE | No measurable speed effect expected. |
| Don't cache failed DNS lookups | Lite | Safe | LOW | A site that briefly failed works again instantly instead of after up to 15 min. |
| Use Cloudflare DNS (1.1.1.1) on all adapters | Lite | Safe | MEDIUM | DNS lookups ≈5–50 ms faster than many ISP resolvers (first visit to each site). In-game ping unchanged. |
| Block ads and trackers with AdGuard DNS (all adapters) | Pro | Moderate | LOW | Ads and trackers blocked for every app at the DNS level, with no hosts-file size limit. Lookup speed about the same as other public DNS. |
| Enable DNS over HTTPS (automatic) | Lite | Safe | NONE | Privacy/security; adds ≈0–5 ms on the first lookup. |
| Prefer IPv4 over IPv6 | Pro | Moderate | LOW | Fixes multi-second connect delays on networks with broken IPv6. 0 ms otherwise. |
| More parallel connections per server | Lite | Safe | LOW | Multi-file downloads in WinINet apps up to 2–4× faster to start. |
| Limit Windows Update background bandwidth to 30% | Lite | Safe | MEDIUM | Windows Update can't saturate your line: avoids 50–500 ms ping spikes while it downloads. |
| Unbind unused network protocols | Pro | Moderate | LOW | A few fewer filter drivers on every packet (µs per packet). Mostly theoretical; tidier stack. |
| Unbind the QoS Packet Scheduler | Pro | Advanced | NONE | One filter driver fewer per packet (µs). No measurable change in games. |
| TCP: more ports, faster port reuse | Lite | Safe | NONE | Helps only with very many connections at once. No ping effect. |
| Network adapter: no flow control or packet batching | Pro | Moderate | LOW | No large-send or receive batching on the adapter. Small effect on ping: game traffic is UDP and light, so the change is usually within noise. |
| Network adapter: interrupt moderation off *(experimental)* | Pro | Moderate | LOW | ≈0.1–1 ms less receive delay per packet burst on Intel / Realtek NICs. Game traffic is only a few hundred packets per second, so the in-game ping change is usually within noise. |
| Network adapter: RSS on a fast core, two queues *(experimental)* | Pro | Moderate | LOW | Measured mechanism: network DPCs leave CPU 0. The gain depends on what else runs on CPU 0. |
| DNS: don't cache network failures | Lite | Safe | NONE | Names resolve at once after a connection drop. No in-game effect. |

## Hardening (13)

| Tweak | Mode | Risk | Impact | Expected gain |
|---|---|---|---|---|
| Disable LLMNR | Lite | Safe | NONE | Security only. |
| Disable SMBv1 server | Lite | Safe | NONE | Security only. |
| Disable NetBIOS over TCP/IP | Lite | Safe | NONE | Security; slightly less LAN chatter. |
| Disable multicast DNS (mDNS) responder | Pro | Moderate | NONE | Less LAN chatter. No speed effect. |
| Block firmware-injected programs (WPBT) | Lite | Safe | LOW | No vendor bloat auto-installed by the firmware (saves RAM and background CPU). |
| Disable Remote Assistance | Lite | Safe | NONE | Security only. |
| NTLMv2 only (no LM / NTLMv1) | Pro | Moderate | NONE | Security only. |
| TLS 1.2+ only (SSL 2/3, TLS 1.0/1.1 off) | Pro | Moderate | NONE | Security only. |
| DNS: no smart multi-adapter resolution, no parallel A/AAAA | Lite | Safe | NONE | Privacy with VPNs. No speed effect. |
| Ignore ICMP redirects | Lite | Safe | NONE | Security only. |
| Block Remote Desktop connections to this PC | Lite | Safe | NONE | Security only. |
| No AutoRun / AutoPlay for any drive | Lite | Safe | NONE | Security only. |
| Don't use the LMHOSTS file | Lite | Safe | NONE | Security only. |

## Tasks & Services (22)

| Tweak | Mode | Risk | Impact | Expected gain |
|---|---|---|---|---|
| Disable telemetry scheduled tasks | Lite | Safe | LOW | Removes periodic background CPU and disk bursts (the Appraiser can use 10–30 % CPU for minutes). |
| Disable non-essential scheduled tasks | Lite | Safe | NONE | Fewer small background jobs. Negligible. |
| Disable Xbox cloud-save task | Pro | Moderate | NONE | Negligible. |
| Disable non-essential services | Lite | Safe | LOW | ≈10–40 MB RAM and a few background wake-ups saved. |
| Disable Hyper-V guest integration services | Lite | Safe | NONE | Negligible on physical PCs (they're idle there). |
| Disable Remote Desktop & remote access services | Pro | Moderate | NONE | Security; ≈10–30 MB RAM. |
| Disable printing services | Pro | Moderate | NONE | ≈10–20 MB RAM; security (PrintNightmare class). |
| Disable Xbox services | Pro | Moderate | NONE | ≈10–30 MB RAM. |
| Disable Bluetooth services | Pro | Moderate | NONE | ≈5–15 MB RAM. |
| Disable diagnostic services | Pro | Moderate | LOW | Fewer diagnostic background scans; ≈20–60 MB RAM. |
| Disable touch keyboard & handwriting service | Pro | Moderate | NONE | ≈10–30 MB RAM. |
| Disable Connected Devices Platform | Pro | Moderate | NONE | ≈10–40 MB RAM and less background network chatter. |
| Disable Killer Network services | Pro | Moderate | MEDIUM | Killer cards: often a steadier ping with the shaping service off, and ≈30–80 MB RAM back. Nothing on other cards. |
| Disable rarely used services (safe tier) | Pro | Moderate | LOW | ≈60–150 MB RAM and a few background wake-ups less. No FPS change. |
| Intel Platform License Manager: start only when needed | Lite | Safe | LOW | Removes a 45 s service timeout at every boot. Desktop and sign-in can respond sooner after a restart. |
| Background disk scans off | Lite | Safe | NONE | No FPS change. Removes rare background disk reads that can cause a hitch on a busy drive. |
| Automatic low-disk cleanup off | Lite | Safe | NONE | No FPS change. Stops a surprise Disk Cleanup run when a drive is nearly full. |
| Speech model downloads off | Lite | Safe | NONE | No speed effect. Less background download. |
| Recovery environment check off | Pro | Moderate | NONE | No speed effect. |
| UPnP device discovery off | Lite | Safe | NONE | No FPS change. Less network broadcast traffic. |
| Windows Image Acquisition off | Lite | Safe | NONE | No speed effect. One service less. |
| Cellular time off | Lite | Safe | NONE | No speed effect. |

## Command tweaks (11)

| Tweak | Mode | Risk | Impact | Expected gain |
|---|---|---|---|---|
| bcdedit: disable dynamic tick *(experimental)* | Pro | Moderate | LOW | Steadier timer ticks. Some users see ≈0.1–0.5 ms lower DPC jitter and smoother frame pacing. More idle power. |
| bcdedit: classic F8 boot menu | Pro | Safe | NONE | Convenience (F8 Safe Mode). Boot ≈1 s slower. |
| fsutil: make sure SSD TRIM is enabled | Pro | Safe | MEDIUM | Stops SSD write speed from degrading 10–50 % over months (if an old tweak disabled TRIM). 0 % if TRIM was already on. |
| netsh: TCP auto-tuning = normal | Pro | Safe | HIGH | If auto-tuning was disabled: downloads can be 2–10× faster on fast or high-latency links. 0 % if it was already normal. |
| netsh: enable Receive-Side Scaling | Pro | Safe | LOW | Multi-gigabit or busy networks: spreads load across cores (lower CPU per packet). Little at 1 Gbps. |
| netsh: disable TCP window-scaling heuristics | Pro | Safe | LOW | Prevents Windows from shrinking the TCP window on 'bad' networks (can recover throughput there). |
| netsh: enable TCP Fast Open | Pro | Safe | LOW | Saves one round-trip (≈10–100 ms) on reconnects to supporting servers. Most games: 0 ms. |
| netsh: disable Teredo tunnelling | Pro | Moderate | NONE | Less background tunnelling. No ping change for most games. |
| powercfg: delete duplicate power plans | Pro | Safe | NONE | Tidiness only. |
| netsh: turn off receive segment coalescing (RSC) | Pro | Moderate | LOW | Windows delivers packets one by one instead of in batches. This can trim tiny receive delays. Within noise on most connections. |
| bcdedit: remove old timer overrides | Pro | Safe | MEDIUM | Only matters if an old guide forced a timer source. Forced HPET: removes ≈1–10 % FPS loss and higher QPC-call overhead. Frame pacing and DPC latency return to normal. |

## Danger zone (24)

| Tweak | Mode | Risk | Impact | Expected gain |
|---|---|---|---|---|
| Disable Defender real-time protection (policy) | Pro | Advanced | HIGH | Real-time scanning off: file-heavy work (installs, compiles, game file checks, shader caches) is ≈10–40 % faster. Games: 0–3 %. |
| Disable Windows Firewall | Pro | Advanced | NONE | No measurable FPS gain (firewall filtering is negligible on modern CPUs). This is a convenience / anti-cheat-compat choice, and it lowers security. |
| Disable User Account Control (UAC) entirely | Pro | Advanced | NONE | No speed effect. Removes all elevation prompts. Significant security reduction; some Store/UWP apps misbehave. |
| Disable SmartScreen for apps & files | Pro | Advanced | LOW | Unknown downloads open ≈0.5–2 s faster (no reputation check). |
| Disable Memory Integrity / VBS | Pro | Advanced | HIGH | Memory Integrity / VBS off: ≈5 % average FPS, up to ≈8–10 % in some games (Tom's Hardware, ComputerBase tests). |
| Disable Spectre / Meltdown OS mitigations | Pro | Advanced | MEDIUM | Older Intel (≤9th gen): 5–20 % in syscall/IO-heavy work, 0–5 % in games. Ryzen and new Intel: usually within noise. |
| Disable Data Execution Prevention (DEP) | Pro | Advanced | LOW | Old 32-bit titles: tiny gains from skipping DEP checks. Modern 64-bit games: about 0. Lowers exploit protection; needs Secure Boot off. |
| Allow unsigned drivers with Secure Boot (WHQL bypass) | Pro | Advanced | NONE | No speed effect. Lets unsigned drivers load with Secure Boot on. |
| Turn off Smart App Control | Pro | Advanced | NONE | Lets blocked apps run. No speed effect. |
| Turn off BitLocker / device encryption | Pro | Advanced | LOW | ≈0–5 % disk throughput on drives without hardware encryption. Games: ≈0 %. |
| Turn off Control Flow Guard (CFG) system-wide | Pro | Advanced | LOW | Removes the CFG check on indirect calls: up to a few % in old games with many indirect calls. Usually within noise. |
| Disks: turn off write-cache buffer flushing | Pro | Advanced | MEDIUM | Write-heavy work (installs, shader compile, unpacking): +5–30 % write speed on SATA drives. Small on NVMe. Risk of data loss on power cut. |
| Native NVMe driver (nvmedisk.sys) — experimental | Pro | Advanced | HIGH | Random 4K I/O: +10–80 % IOPS in published tests, with lower CPU per I/O. Real-world: slightly faster boot, load and shader-cache reads. Sequential speed unchanged. |
| bcdedit: don't start the hypervisor | Pro | Advanced | MEDIUM | Removes hypervisor overhead: ≈2–8 % in some games, lower DPC latency. Also turns off VBS / Memory Integrity. |
| Disable CPU idle states (C-states) — EXTREME *(experimental)* | Pro | Advanced | LOW | Removes C-state exit latency (≈2–100 µs per wake). DPC latency drops slightly. FPS can go DOWN 1–5 % on modern CPUs, because heat uses up the boost headroom. Benchmark before you keep it. |
| Disable Modern Standby (S0) → classic S3 sleep | Pro | Advanced | NONE | Battery drain and unwanted wake-ups in sleep. No in-use speed effect. |
| Disable IPv6 on all adapters | Pro | Advanced | NONE | No gain on working networks. Can break things. |
| Lock out Windows & Alt keyboard shortcuts | Pro | Advanced | NONE | No speed effect. Prevents accidental Alt-Tab / Windows-key minimise during fullscreen play. |
| Disable OEM audio effect services | Pro | Advanced | MEDIUM | Systems with Nahimic or similar: removes a known cause of game stutter and crashes, and ≈50–150 MB RAM. Nothing where none is installed. |
| Disable more services (warning tier) | Pro | Advanced | LOW | ≈30–80 MB RAM more. No FPS change. |
| NVIDIA: force Resizable BAR in every game *(experimental)* | Pro | Advanced | LOW | −5 % to +10 % FPS depending on the game. Measure each one. |
| Block all outgoing NTLM | Pro | Advanced | NONE | Security only. |
| NVIDIA driver: GPU PCIe power saving off, per-core DPCs *(experimental)* | Pro | Advanced | LOW | Unproven: a lower GPU DPC peak on some systems. |
| Disable the High Precision Event Timer device *(experimental)* | Pro | Advanced | NONE | Usually none on modern systems; contested. |
