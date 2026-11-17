// ArchTweaker 1.2 additions: power plan, input & latency, GPU & devices, and extra tweaks
// researched from public guides and the WinSux script (MIT).
using System;
using System.Linq;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static partial class Catalog
    {
        const string PROC = "SUB_PROCESSOR", DISK = "SUB_DISK", SLEEP = "SUB_SLEEP", PCIE = "SUB_PCIEXPRESS";
        const string USB = "2a737441-1930-4402-8d77-b2bebba308a3";
        public const string MAXPLAN = "a7c7e000-2026-4c0d-9e11-a7c7e0002026";

        static void AddMore()
        {
            const Hive LM = Hive.HKLM, CU = Hive.HKCU;
            string PP = Cat.PowerPlan, IL = Cat.Input, GD = Cat.Gpu;
            string S = Cat.System, RS = Cat.Responsiveness, N = Cat.Network, X = DangerZone.CAT;
            string safe = PRESET_SAFE, game = PRESET_GAMING, priv = PRESET_PRIVACY;

            // ================= POWER PLAN =================
            T("pp_plan", PP, "Create & activate 'ArchTweaker Max Performance' plan", "Clones Microsoft's hidden Ultimate Performance plan (or High Performance on editions that block it) into its own plan and makes it active. Every other Power Plan tweak below changes the ACTIVE plan, so apply this one first. The app does that automatically when you tick several.")
                .Info("Why: Balanced lets the CPU drop to low clocks and park cores, and every ramp-up back to full speed costs a little latency. Ultimate Performance removes most of those power-saving steps. Expect 10–30 W more at idle on a desktop.")
                .Explains("Runs: powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 " + MAXPLAN,
                          "Runs: powercfg -changename " + MAXPLAN + " \"ArchTweaker Max Performance\"",
                          "Runs: powercfg /setactive " + MAXPLAN,
                          "Undo: switches back to Balanced and deletes the ArchTweaker plan")
                .DetectBy(() => PowerCache.ActiveScheme().Equals(MAXPLAN, StringComparison.OrdinalIgnoreCase))
                .OnApply(PowerPlans.CreateMax)
                .OnRevert(PowerPlans.RemoveMax)
                .In(game);
            T("pp_minmax", PP, "CPU minimum & maximum state 100%", "Keeps the CPU at full speed instead of letting it drop to minimum clocks between frames. Modern CPUs still lower voltage on their own when idle (C-states), so this is safe.")
                .Pow(PROC, "PROCTHROTTLEMIN", 100, "Minimum processor state", "100 %")
                .Pow(PROC, "PROCTHROTTLEMAX", 100, "Maximum processor state", "100 %")
                .In(game);
            T("pp_boost", PP, "Processor boost mode: Aggressive", "Windows requests turbo boost as soon as there's any load, instead of waiting to see if the load lasts. Unlocks the hidden 'Processor performance boost mode' setting.")
                .Pow(PROC, "PERFBOOSTMODE", 2, "Processor performance boost mode", "Aggressive")
                .Pow(PROC, "PERFBOOSTPOL", 100, "Processor performance boost policy", "100 %")
                .In(game);
            T("pp_epp", PP, "Energy Performance Preference = 0 (maximum performance)", "Tells the CPU's own hardware governor (Intel Speed Shift / AMD CPPC) to always pick performance over power saving. This is the setting that matters most on modern CPUs. P-cores and E-cores are both covered.")
                .Pow(PROC, "PERFEPP", 0, "Energy performance preference", "0 % (max performance)")
                .Pow(PROC, "PERFEPP1", 0, "Energy performance preference — class 1 (P-cores)", "0 %")
                .In(game);
            T("pp_parking", PP, "Disable CPU core parking", "All cores stay unparked, so there's no delay waking a parked core when a game suddenly needs more threads.")
                .Pow(PROC, "CPMINCORES", 100, "Core parking min cores", "100 %")
                .Pow(PROC, "CPMINCORES1", 100, "Core parking min cores — class 1", "100 %")
                .Pow(PROC, "CPMAXCORES", 100, "Core parking max cores", "100 %")
                .Pow(PROC, "CPMAXCORES1", 100, "Core parking max cores — class 1", "100 %")
                .In(game);
            T("pp_ramp", PP, "Instant clock ramp-up (Rocket policy)", "When load appears, the CPU jumps straight to maximum clocks instead of stepping up gradually. It checks load every 5 ms instead of every 15–30 ms. Mostly matters on systems that don't use autonomous (hardware) P-states.")
                .Pow(PROC, "PERFINCPOL", 2, "Performance increase policy", "Rocket")
                .Pow(PROC, "PERFDECPOL", 1, "Performance decrease policy", "Single")
                .Pow(PROC, "PERFINCTHRESHOLD", 10, "Performance increase threshold", "10 %")
                .Pow(PROC, "PERFDECTHRESHOLD", 8, "Performance decrease threshold", "8 %")
                .Pow(PROC, "PERFINCPOL1", 2, "Performance increase policy — class 1", "Rocket")
                .Pow(PROC, "PERFDECPOL1", 1, "Performance decrease policy — class 1", "Single")
                .Pow(PROC, "PERFCHECK", 5, "Performance time check interval", "5 ms")
                .In(game);
            T("pp_latency", PP, "Latency-sensitivity hints → 100%", "When you click, press Enter or touch, Windows sends a 'latency-sensitive' hint. This makes those hints raise performance all the way to 100% and unpark every core.")
                .Pow(PROC, "LATENCYHINTPERF", 100, "Latency sensitivity hint processor performance", "100 %")
                .Pow(PROC, "LATENCYHINTPERF1", 100, "Latency sensitivity hint processor performance — class 1", "100 %")
                .Pow(PROC, "LATENCYHINTUNPARK", 100, "Latency sensitivity hint min unparked cores", "100 %")
                .Pow(PROC, "LATENCYHINTUNPARK1", 100, "Latency sensitivity hint min unparked cores — class 1", "100 %")
                .In(game);
            T("pp_nothrottle", PP, "No throttle states, no duty cycling, active cooling", "Disables the legacy T-state clock throttling and duty-cycling. Cooling policy is set to Active, so fans speed up before clocks drop.")
                .Pow(PROC, "THROTTLING", 0, "Allow throttle states", "Off")
                .Pow(PROC, "DUTYCYCLING", 0, "Processor duty cycling", "Disabled")
                .Pow(PROC, "SYSCOOLPOL", 1, "System cooling policy", "Active")
                .In(game);
            T("pp_hybrid", PP, "Hybrid CPUs: prefer P-cores for all threads", "Intel 12th gen and newer, and other hybrid CPUs: the scheduler puts long-running and short-running threads on the fast P-cores first. Has no effect on CPUs with only one core type.")
                .Pow(PROC, "SCHEDPOLICY", 2, "Heterogeneous thread scheduling policy", "Prefer performant processors")
                .Pow(PROC, "SHORTSCHEDPOLICY", 2, "Heterogeneous short running thread scheduling policy", "Prefer performant processors")
                .R(Risk.Moderate).In(game);
            T("pp_idle", PP, "Disable CPU idle states (C-states) — EXTREME", "Cores never sleep. They spin at full power, which removes C-state exit latency fully. Downsides: much higher temperature and power draw, and Task Manager shows 100% CPU. Modern CPUs can also boost LOWER because they use up thermal and power headroom. Benchmark before you keep it.")
                .Pow(PROC, "IDLEDISABLE", 1, "Processor idle disable", "Idle disabled")
                .R(Risk.Advanced);
            T("pp_pcie", PP, "PCIe Link State Power Management off", "Your GPU and NVMe links never drop into ASPM low-power states, so they don't take time to wake up. Matches the BIOS 'ASPM off' advice.")
                .Pow(PCIE, "ASPM", 0, "Link state power management", "Off")
                .In(game);
            T("pp_usb", PP, "USB: no selective suspend, no USB3 link power saving", "Mice, keyboards, headsets and DACs are never powered down. Avoids input and audio dropouts after idle.")
                .Pow(USB, "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0, "USB selective suspend", "Disabled")
                .Pow(USB, "0853a681-27c8-4100-a2fd-82013e970683", 0, "USB hub selective suspend timeout", "0")
                .Pow(USB, "d4e98f31-5ffe-4ce1-be31-1b38b384c009", 0, "USB 3 link power management", "Off")
                .In(game);
            T("pp_disk", PP, "Disks never power down (HDD, SATA link, NVMe idle)", "No spin-down or link sleep, so no stall the first time a game reads from an idle drive.")
                .Pow(DISK, "DISKIDLE", 0, "Turn off hard disk after", "Never")
                .Pow(DISK, "0b2d69d7-a2a1-449c-9680-f91c70521c60", 0, "AHCI link power management (HIPM/DIPM)", "Active")
                .Pow(DISK, "d639518a-e56d-4345-8af2-b9f32fb26109", 0, "Primary NVMe idle timeout", "0 (no idle transition)")
                .In(game);
            T("pp_wifi", PP, "Wi-Fi adapter: maximum performance", "The wireless card never enters power-save mode, which causes ping spikes.")
                .Pow("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1", "12bbebe6-58d6-4636-95bb-3217ef867c1a", 0, "Wireless adapter power saving mode", "Maximum performance")
                .In(game);
            T("pp_gpu", PP, "GPU power settings: maximum performance", "Sets the Intel graphics power plan, AMD power slider, ATI PowerPlay and switchable-graphics policy to maximum performance. ArchTweaker skips settings your hardware doesn't have.")
                .Pow("44f3beca-a7c0-460e-9df2-bb8b99e0cba6", "3619c3f2-afb2-4afc-b0e9-e7fef372de36", 2, "Intel graphics power plan", "Maximum performance")
                .Pow("c763b4ec-0e50-4b6b-9bed-2b92a6ee884e", "7ec1751b-60ed-4588-afb5-9819d3d77d90", 3, "AMD power slider overlay", "Best performance")
                .Pow("f693fb01-e858-4f00-b20f-f30e12ac06d6", "191f65b5-d45c-4a4f-8aae-1ab8bfd980e6", 1, "ATI PowerPlay", "Maximize performance")
                .Pow("e276e160-7cb0-43c6-b20b-73f5dce39954", "a1662ab2-9d34-4e53-ba8b-2639b9e20857", 3, "Switchable dynamic graphics", "Maximize performance")
                .In(game);
            T("pp_video", PP, "Video playback: performance bias", "Video playback stops trading quality or clocks for power.")
                .Pow("9596fb26-9850-41fd-ac3e-f7c3c00afd4b", "10778347-1370-4ee0-8bbd-33bdacaade49", 1, "Video playback quality bias", "Performance bias")
                .Pow("9596fb26-9850-41fd-ac3e-f7c3c00afd4b", "34c7b99f-9a6d-4b3c-8dc7-b6693b78cef4", 0, "When playing video", "Optimize video quality")
                .In(game);
            T("pp_sleep", PP, "Never sleep / hibernate, no hybrid sleep, no wake timers", "The PC stays on until you shut it down. Wake timers can't start it at night for maintenance.")
                .Pow(SLEEP, "STANDBYIDLE", 0, "Sleep after", "Never")
                .Pow(SLEEP, "HYBRIDSLEEP", 0, "Allow hybrid sleep", "Off")
                .Pow(SLEEP, "HIBERNATEIDLE", 0, "Hibernate after", "Never")
                .Pow(SLEEP, "RTCWAKE", 0, "Allow wake timers", "Disable")
                .R(Risk.Moderate);
            T("pp_unhide", PP, "Show all hidden power settings in Control Panel", "Every hidden option (boost mode, EPP, core parking, idle disable…) appears in powercfg.cpl → Advanced settings, so you can fine-tune it yourself.")
                .Dev("Power setting visibility", Devices.HiddenPowerSettings, "Attributes", RegistryValueKind.DWord, 2, true);
            T("pp_modernstandby", PP, "Disable Modern Standby (S0) → classic S3 sleep", "On laptops and newer boards, Modern Standby keeps the PC half-awake while 'asleep', so it drains battery and wakes up for updates. Classic S3 sleep only works if your firmware supports it (check with powercfg /a).")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\Power", "PlatformAoAcOverride", 0, null)
                .NeedsReboot().R(Risk.Advanced);

            // ================= INPUT & LATENCY =================
            T("il_timerres", IL, "Timer resolution 0.5 ms (background service)", "Installs a tiny service, built into ArchTweaker itself, that keeps the Windows system timer at 0.5 ms instead of 15.6 ms. On Windows 11 it also turns on GlobalTimerResolutionRequests, so the request applies system-wide. Result: more precise Sleep() and frame pacing in games that don't request it themselves.")
                .Info("Evidence: timer-resolution benchmarks (MeasureSleep, TimerResBenchmark) show Sleep(1) jitter dropping from ~1–2 ms to ~0.5 ms. Cost: slightly more CPU wake-ups and power. Combine it with 'bcdedit: disable dynamic tick' in Command tweaks.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests", 1, null)
                .Explains("Copies ArchTweaker.exe to C:\\ProgramData\\ArchTweaker\\bin\\ and creates the auto-start service '" + TimerRes.SVC + "' (runs ArchTweaker.exe /timerservice, which calls NtSetTimerResolution(5000)).",
                          "Undo: ArchTweaker stops and deletes the service.")
                .DetectBy(() => Svc.Exists(TimerRes.SVC) && Reg.Norm(Reg.Get(LM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests")) == "1")
                .OnApply(TimerRes.Install).OnRevert(TimerRes.Uninstall)
                .NeedsReboot().In(game);
            T("il_queue", IL, "Smaller mouse & keyboard input buffers", "MouseDataQueueSize and KeyboardDataQueueSize go from 100 to 20 packets. Popular in latency guides, but the evidence is anecdotal: it's a buffer size, not a polling rate. Values that are too small can drop input.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "MouseDataQueueSize", 20, 100)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "KeyboardDataQueueSize", 20, 100)
                .NeedsReboot().R(Risk.Moderate);
            T("il_rawthrottle", IL, "Remove background raw-mouse throttle (Windows 11)", "Since 2023, Windows 11 caps how often BACKGROUND apps receive high-polling-rate mouse input (to ~125 Hz). This removes the cap. Only useful if a background tool, like an overlay or macro app, needs full-rate input. Your focused game is never throttled, and Microsoft added the cap to REDUCE stutter.")
                .Dw(CU, @"Control Panel\Mouse", "RawMouseThrottleEnabled", 0, null)
                .W11().R(Risk.Moderate);
            T("il_usbpower", IL, "USB & HID devices: no power saving", "Per-device setting: USB and HID devices (mouse, keyboard, headset, controller) never use enhanced power management or idle-in-working-state. Complements the USB power-plan tweak.")
                .Dev("USB/HID enhanced power management", () => Devices.DeviceParams("USB", "HID"), "EnhancedPowerManagementEnabled", RegistryValueKind.DWord, 0, false)
                .Dev("USB/HID selective suspend", () => Devices.DeviceParams("USB", "HID"), "SelectiveSuspendOn", RegistryValueKind.DWord, 0, true)
                .Dev("USB/HID idle in working state", () => Devices.DeviceWdf("USB", "HID"), "IdleInWorkingState", RegistryValueKind.DWord, 0, false)
                .NeedsReboot().In(game);

            // ================= GPU & DEVICES =================
            T("gd_msi", GD, "GPU: Message-Signaled Interrupts (MSI mode)", "Switches the graphics card from shared line-based interrupts to MSI. MSI gives lower and steadier interrupt latency (DPC). Modern NVIDIA and AMD drivers usually use MSI already, and this makes sure of it. Very old GPUs can misbehave. Undo restores the original value.")
                .Dev("GPU MSISupported", Devices.GpuMsiKeys, "MSISupported", RegistryValueKind.DWord, 1, false)
                .NeedsReboot().R(Risk.Moderate).In(game);
            T("gd_nv_p0", GD, "NVIDIA: lock highest performance state (P0)", "The GPU stays at full boost clocks and never downclocks (DisableDynamicPstate = 1). Removes clock ramp-up latency. Costs much higher idle power (+20–60 W) and heat, and the fans may never stop.")
                .Dev("NVIDIA DisableDynamicPstate", Devices.NvidiaKeys, "DisableDynamicPstate", RegistryValueKind.DWord, 1, false)
                .Requires(Devices.HasNvidia, "NVIDIA GPU").NeedsReboot().R(Risk.Advanced);
            T("gd_nv_hdcp", GD, "NVIDIA: disable HDCP", "Turns off HDCP copy protection in the driver (RMHdcpKeyglobZero). Some guides claim lower latency, but there's no solid evidence. It BREAKS protected playback: Netflix/Disney+ HD, Blu-ray, some capture setups.")
                .Dev("NVIDIA RMHdcpKeyglobZero", Devices.NvidiaKeys, "RMHdcpKeyglobZero", RegistryValueKind.DWord, 1, false)
                .Requires(Devices.HasNvidia, "NVIDIA GPU").NeedsReboot().R(Risk.Moderate);
            T("gd_nv_misc", GD, "NVIDIA: PhysX on GPU, no tray icon, developer settings", "Forces PhysX onto the GPU instead of 'auto' (which can pick the CPU). Removes the NVIDIA tray icon at login. Shows the Developer menu and lets all users read GPU performance counters (useful for PresentMon, FrameView, CapFrameX).")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Parameters\Global\NVTweak", "NvCplPhysxAuto", 0, null)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Parameters\Global\NVTweak", "NvDevToolsVisible", 1, null)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Parameters\Global\NVTweak", "RmProfilingAdminOnly", 0, null)
                .Dw(CU, @"Software\NVIDIA Corporation\NvTray", "StartOnLogin", 0, null)
                .Requires(Devices.HasNvidia, "NVIDIA GPU").In(game);
            T("gd_nv_telemetry", GD, "NVIDIA: turn off telemetry and update checks", "Disables NVIDIA's telemetry and crash-report scheduled tasks (NvTmRep, NvTmMon), its daily driver-update and profile-updater checks, and the old NvTelemetryContainer service. Game profiles and the driver itself stay the same. Check for driver updates yourself.")
                .Tasks(Devices.NvidiaTasks()).Service("NvTelemetryContainer", 4, 2)
                .Requires(() => Devices.HasNvidia() && Devices.NvidiaTasks().Length > 0, "NVIDIA GPU with NVIDIA's scheduled tasks").In(game, priv);
            // NVIDIA driver profile (global/base profile, same settings as NVIDIA Control Panel → Manage 3D settings)
            T("gd_nv_drs_power", GD, "NVIDIA: power management mode = Prefer maximum performance", "Sets the driver's global 3D profile so games hold high GPU clocks. The default 'Normal' (optimal power) can drop clocks between frames in light or CPU-bound games. Desktop and video still downclock. Same as NVIDIA Control Panel → Manage 3D settings → Power management mode.")
                .Nv(NvDrs.PREFERRED_PSTATE, () => NvDrs.PSTATE_PREFER_MAX, "Power management mode", "Prefer maximum performance")
                .Requires(() => NvDrs.Available, "NVIDIA driver").In(game);
            T("gd_nv_drs_latency", GD, "NVIDIA: low latency mode = On", "Limits the CPU to queuing 1 frame ahead of the GPU (Max pre-rendered frames = 1) in DX11 games that don't use Reflex. Lowers input lag when the GPU is the bottleneck. Games with NVIDIA Reflex override this.")
                .Nv(NvDrs.PRERENDERLIMIT, () => 1u, "Low latency mode", "On (1 pre-rendered frame)")
                .Requires(() => NvDrs.Available, "NVIDIA driver").In(game);
            T("gd_nv_drs_shader", GD, "NVIDIA: shader cache size = Unlimited", "Stops the driver from deleting compiled shaders when the cache reaches its size limit. Games then recompile less and stutter less after patches or driver updates. Uses more disk space over time.")
                .Nv(NvDrs.SHADERCACHE_MAX, () => 0xFFFFFFFFu, "Shader cache size", "Unlimited")
                .Requires(() => NvDrs.Available, "NVIDIA driver").In(game);
            T("gd_nv_drs_frl", GD, "NVIDIA: cap frame rate 3 below refresh (G-Sync / VRR)", "Sets the driver's Max frame rate to your refresh rate minus 3 (for example 237 fps at 240 Hz). Then the GPU stays inside the G-Sync / FreeSync range and never hits V-Sync lag. Only useful on a variable-refresh display. Competitive players who want uncapped fps should leave this off.")
                .Info("ArchTweaker calculates the cap from the primary display's current refresh rate when you apply. Apply again after you change the refresh rate.")
                .Nv(NvDrs.FRL_FPS, () => { int hz = NvDrs.RefreshHz(); return hz > 30 ? (uint)(hz - 3) : 0u; }, "Max frame rate", "refresh − 3 fps")
                .Requires(() => NvDrs.Available, "NVIDIA driver").R(Risk.Moderate);
            T("gd_amd_ulps", GD, "AMD: disable ULPS (Ultra Low Power State)", "Stops the AMD GPU (or the secondary GPU in multi-GPU setups) from dropping into its deepest sleep state. That state can cause wake-up stutter, black screens and overclock-tool problems.")
                .Dev("AMD EnableUlps", Devices.AmdGpuKeys, "EnableUlps", RegistryValueKind.DWord, 0, true)
                .Requires(Devices.HasAmdGpu, "AMD GPU").NeedsReboot().R(Risk.Moderate);
            T("gd_pcipower", GD, "PCI & ACPI devices: no power saving", "Per-device setting: PCI and ACPI devices (GPU, NVMe, NIC, USB controllers) don't use enhanced power management or idle in working state.")
                .Dev("PCI/ACPI enhanced power management", () => Devices.DeviceParams("PCI", "ACPI"), "EnhancedPowerManagementEnabled", RegistryValueKind.DWord, 0, false)
                .Dev("PCI/ACPI idle in working state", () => Devices.DeviceWdf("PCI", "ACPI"), "IdleInWorkingState", RegistryValueKind.DWord, 0, false)
                .NeedsReboot().R(Risk.Moderate).In(game);
            T("gd_wake", GD, "Devices can't wake the PC", "USB, HID, PCI and ACPI devices can no longer wake the PC from sleep. No more random wake-ups from a bumped mouse or a network packet.")
                .Dev("Device wake", () => Devices.DeviceParams("USB", "HID", "PCI", "ACPI"), "WaitWakeEnabled", RegistryValueKind.DWord, 0, false)
                .NeedsReboot();
            T("gd_nicpower", GD, "Network adapters: no power saving, no Green Ethernet, no wake", "Every physical Ethernet/Wi-Fi adapter: Windows can't turn it off to save power (PnPCapabilities = 24). ArchTweaker turns off Energy-Efficient Ethernet, Green Ethernet, Gigabit Lite, ultra-low-power mode and Wake-on-LAN wherever the driver supports them. EEE in particular adds small latency spikes.")
                .Dev("NIC PnPCapabilities", Devices.NicKeys, "PnPCapabilities", RegistryValueKind.DWord, 24, false)
                .Dev("NIC Energy-Efficient Ethernet", Devices.NicKeys, "*EEE", RegistryValueKind.String, "0", true)
                .Dev("NIC EEE link advertisement", Devices.NicKeys, "EEELinkAdvertisement", RegistryValueKind.String, "0", true)
                .Dev("NIC Advanced EEE", Devices.NicKeys, "AdvancedEEE", RegistryValueKind.String, "0", true)
                .Dev("NIC Green Ethernet", Devices.NicKeys, "EnableGreenEthernet", RegistryValueKind.String, "0", true)
                .Dev("NIC Gigabit Lite", Devices.NicKeys, "GigaLite", RegistryValueKind.String, "0", true)
                .Dev("NIC power saving mode", Devices.NicKeys, "PowerSavingMode", RegistryValueKind.String, "0", true)
                .Dev("NIC ultra low power", Devices.NicKeys, "ULPMode", RegistryValueKind.String, "0", true)
                .Dev("NIC system idle power saver", Devices.NicKeys, "SipsEnabled", RegistryValueKind.String, "0", true)
                .Dev("NIC wake on magic packet", Devices.NicKeys, "*WakeOnMagicPacket", RegistryValueKind.String, "0", true)
                .Dev("NIC wake on pattern", Devices.NicKeys, "*WakeOnPattern", RegistryValueKind.String, "0", true)
                .Dev("NIC S5 wake on LAN", Devices.NicKeys, "S5WakeOnLan", RegistryValueKind.String, "0", true)
                .Dev("NIC wake on link", Devices.NicKeys, "WakeOnLink", RegistryValueKind.String, "0", true)
                .Dev("NIC modern-standby WoL", Devices.NicKeys, "*ModernStandbyWoLMagicPacket", RegistryValueKind.String, "0", true)
                .NeedsReboot().In(game);
            T("gd_writecache", GD, "Disks: turn off write-cache buffer flushing", "Windows stops forcing the drive to flush its write cache (CacheIsPowerProtected = 1). Writes get faster, but a power cut or crash can lose or corrupt recently written data. Only use this with a UPS or on a laptop battery.")
                .Dev("Disk CacheIsPowerProtected", Devices.DiskCacheKeys, "CacheIsPowerProtected", RegistryValueKind.DWord, 1, false)
                .NeedsReboot().R(Risk.Advanced);
            T("gd_nvme", GD, "Native NVMe driver (nvmedisk.sys) — experimental", "Enables Microsoft's new native NVMe stack from Windows Server 2025. Tests show big random-I/O gains. SSDs then show under 'Storage disks'. Some vendor tools (for example Samsung Magician) and backup apps can fail to recognise the drive. Newer Windows updates can also ignore the switch without a message. ArchTweaker adds safe-mode boot keys too.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Policies\Microsoft\FeatureManagement\Overrides", "735209102", 1, null)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Policies\Microsoft\FeatureManagement\Overrides", "3244671118", 1, null)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Policies\Microsoft\FeatureManagement\Overrides", "1853569164", 1, null)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Policies\Microsoft\FeatureManagement\Overrides", "156965516", 1, null)
                .Sz(LM, @"SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal\{75416E63-5912-4DFA-AE8F-3EFACCAFFB14}", "", "Storage disks", null)
                .Sz(LM, @"SYSTEM\CurrentControlSet\Control\SafeBoot\Network\{75416E63-5912-4DFA-AE8F-3EFACCAFFB14}", "", "Storage disks", null)
                .W11().NeedsReboot().R(Risk.Advanced);

            // ================= more SYSTEM / RESPONSIVENESS =================
            T("rs_visualfx", RS, "Visual effects: best performance (keeps smooth fonts)", "Same as 'Adjust for best performance' in System Properties. It turns off fading and sliding menus, shadows, window contents while dragging, translucent selection and Aero Peek. ClearType font smoothing stays on.")
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 3, null)
                .Bin(CU, @"Control Panel\Desktop", "UserPreferencesMask", new byte[] { 0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00 }, new byte[] { 0x9E, 0x1E, 0x07, 0x80, 0x12, 0x00, 0x00, 0x00 })
                .Sz(CU, @"Control Panel\Desktop", "DragFullWindows", "0", "1")
                .Dw(CU, ADV, "ListviewAlphaSelect", 0, 1)
                .Dw(CU, ADV, "ListviewShadow", 0, 1)
                .Dw(CU, @"Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0, 1)
                .Dw(CU, @"Software\Microsoft\Windows\DWM", "AlwaysHibernateThumbnails", 0, null)
                .Sz(CU, @"Control Panel\Desktop", "FontSmoothing", "2", "2")
                .NeedsRelogon().In(game);
            T("rs_inputpreload", RS, "Don't preload input experience & widgets", "Windows stops pre-launching the touch-keyboard/emoji host (TextInputHost) and the widgets board in the background.")
                .Dw(CU, @"Software\Microsoft\input", "IsInputAppPreloadEnabled", 0, null)
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Dsh", "IsPrelaunchEnabled", 0, null)
                .In(safe, game);
            T("sys_maintenance", S, "Disable automatic maintenance", "Stops Windows from running its idle maintenance bundle (diagnostics, scans, updates, drive optimisation) when you step away. That work can still run when you come back to a game. Note: 'Optimize drives' (SSD TRIM) is part of maintenance. Run it yourself from time to time (Tools & backup → Optimize drives).")
                .Dw(LM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "MaintenanceDisabled", 1, null)
                .R(Risk.Moderate);
            T("sys_bsod", S, "Show technical details on blue screens", "Blue screens show the stop code parameters and driver name, which makes crashes much easier to diagnose.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\CrashControl", "DisplayParameters", 1, null).In(safe);
            T("sys_wpbt", S, "Block firmware-injected programs (WPBT)", "Stops the motherboard firmware from making Windows auto-run vendor software (Armoury Crate / driver installers and the like) on every boot.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\Session Manager", "DisableWpbtExecution", 1, null).In(safe, priv, game);
            T("sys_pauseupdates", S, "Pause Windows Update for 1 year", "Feature and quality updates are paused for 365 days from the moment you apply this. Unticking resumes them. Remember to update manually for security fixes.")
                .Explains("Writes PauseUpdatesStartTime / PauseUpdatesExpiryTime, PauseFeatureUpdates* and PauseQualityUpdates* (UTC dates) under HKLM\\SOFTWARE\\Microsoft\\WindowsUpdate\\UX\\Settings   ·   undo: deletes these values")
                .DetectBy(() => { object o = Reg.Get(LM, @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "PauseUpdatesExpiryTime"); DateTime d; return o != null && DateTime.TryParse(o.ToString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out d) && d > DateTime.UtcNow; })
                .OnApply(Updates.Pause).OnRevert(Updates.Resume)
                .R(Risk.Moderate);
            T("net_remoteassist", N, "Disable Remote Assistance", "Nobody can use Windows Remote Assistance to view or control this PC.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", 0, 1).In(safe, priv);
            // 3.30: the undo did not bind ms_implat again, and the QoS Packet Scheduler moved to its own tweak
            T("net_bindings", N, "Unbind unused network protocols", "Removes LLDP, Link-Layer Topology Discovery (mapper and responder) and the Network Adapter Multiplexor from your adapters. Then less code runs on every packet. File sharing, IPv6 and the QoS Packet Scheduler stay the same. The network map in old Control Panel stops working.")
                .PS("foreach ($c in 'ms_lldp','ms_lltdio','ms_rspndr','ms_implat') { Disable-NetAdapterBinding -Name '*' -ComponentID $c -ErrorAction SilentlyContinue }")
                .UndoPS("foreach ($c in 'ms_lldp','ms_lltdio','ms_rspndr','ms_implat') { Enable-NetAdapterBinding -Name '*' -ComponentID $c -ErrorAction SilentlyContinue }")
                // versions before 3.30 also unbound ms_pacer: bind it again, unless its own tweak is applied
                .OnRevert(() => { if (!AppliedSet.Read().Contains("net_pacer")) Shell.PS("Enable-NetAdapterBinding -Name '*' -ComponentID ms_pacer -ErrorAction SilentlyContinue"); })
                .R(Risk.Moderate).In(game);
            T("net_pacer", N, "Unbind the QoS Packet Scheduler", "Removes the QoS Packet Scheduler (ms_pacer) from your adapters. A little less code runs on every packet. Windows QoS policies and DSCP marking stop working, and some routers' QoS for games and calls relies on them.")
                .PS("Disable-NetAdapterBinding -Name '*' -ComponentID ms_pacer -ErrorAction SilentlyContinue")
                .UndoPS("Enable-NetAdapterBinding -Name '*' -ComponentID ms_pacer -ErrorAction SilentlyContinue")
                .R(Risk.Advanced);
            T("net_ipv6off", N, "Disable IPv6 on all adapters", "Unbinds IPv6 from every adapter. Some ISPs (and Xbox Live / Teredo, HomeGroup, DirectAccess) need IPv6, so try 'Prefer IPv4' first.")
                .PS("Disable-NetAdapterBinding -Name '*' -ComponentID ms_tcpip6 -ErrorAction SilentlyContinue")
                .UndoPS("Enable-NetAdapterBinding -Name '*' -ComponentID ms_tcpip6 -ErrorAction SilentlyContinue")
                .R(Risk.Advanced);

            // ================= SECURITY (ADVANCED) =================
            T("sec_spectre", X, "Disable Spectre / Meltdown OS mitigations", "FeatureSettingsOverride = 3. Biggest gains (5–20% in syscall- or IO-heavy work) are on older Intel CPUs (up to ~9th gen). On Ryzen and recent Intel the difference is usually within noise. This re-opens real side-channel attacks against your browser and other apps.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettingsOverride", 3, null)
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettingsOverrideMask", 3, null)
                .NeedsReboot().R(Risk.Advanced);
        }
    }

    public static class Updates
    {
        const string P = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
        static readonly string[] Names = { "PauseUpdatesStartTime", "PauseUpdatesExpiryTime", "PauseFeatureUpdatesStartTime", "PauseFeatureUpdatesEndTime", "PauseQualityUpdatesStartTime", "PauseQualityUpdatesEndTime" };
        public static void Pause()
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), end = DateTime.UtcNow.AddDays(365).ToString("yyyy-MM-ddTHH:mm:ssZ");
            foreach (var n in Names) Reg.Set(Hive.HKLM, P, n, RegistryValueKind.String, n.Contains("Start") ? now : end);
        }
        public static void Resume() { foreach (var n in Names) Reg.DeleteValue(Hive.HKLM, P, n); }
    }
}
