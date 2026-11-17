// ArchTweaker: Tweaks added in 3.8.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace ArchTweaker
{
    // 3.9: research-driven defaults (Nov 2026 audit of a 12900K / RTX 3080 Ti PC)
    public static partial class Catalog
    {
        // Unproven or harmful in measurements: they stay available, but only as "measure first" (experimental, no preset).
        // C-states off: microsecond exit latency, less boost headroom. Dynamic tick off: timer issues reported on Windows 11.
        // Forced ReBAR: NVIDIA's own note says some games lose performance.
        internal static readonly string[] MeasureFirstIds = { "pp_idle", "cmd_dynamictick", "dz_nv_rebar" };
        static void MeasureFirst()
        {
            foreach (var t in all) if (MeasureFirstIds.Contains(t.Id)) t.Experimental = true;
        }

        static readonly Dictionary<string, object[]> GainsV39 = new Dictionary<string, object[]>
        {
            { "net_itr_off", new object[] { 1, "≈0.1–1 ms less receive delay per packet burst on Intel / Realtek NICs. Game traffic is only a few hundred packets per second, so the in-game ping change is usually within noise." } },
            { "svc_plm_manual", new object[] { 1, "Removes a 45 s service timeout at every boot. Desktop and sign-in can respond sooner after a restart." } },
        };

        // Display name of Intel's Platform License Manager service; its key name differs between driver packages.
        internal static string PlmService()
        {
            const string S = @"SYSTEM\CurrentControlSet\Services\";
            try
            {
                foreach (var k in Reg.SubKeys(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services"))
                {
                    string dn = (Reg.Get(Hive.HKLM, S + k, "DisplayName") ?? "").ToString(), img = (Reg.Get(Hive.HKLM, S + k, "ImagePath") ?? "").ToString();
                    if (dn.IndexOf("Platform License Manager", StringComparison.OrdinalIgnoreCase) >= 0 || img.IndexOf("PlatformLicenseManager", StringComparison.OrdinalIgnoreCase) >= 0) return k;
                }
            }
            catch { }
            return null;
        }

        // 3.15: tweaks from the November 2026 registry research. Each has a documented mechanism or a measurement
        // by a named tester. None is in a preset: measure with the Latency and Measure pages.
        static void AddV315()
        {
            string N = Cat.Network, GD = Cat.Gpu, IL = Cat.Input;
            const string KERNEL = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";

            var au = T("gd_audio_idle", GD, "Audio devices: no idle power-down", "Sets the idle timers of each audio device (PerformanceIdleTime and ConservationIdleTime) to 0. The codec then stays awake between sounds. This removes the short delay or the cut-off first syllable when a sound starts after silence. Covers the motherboard codec and the GPU's HDMI/DisplayPort audio.")
                .Info("Microsoft's audio driver documentation: \"Specify zero to disable idle detection.\" NVIDIA's support article 5441 gives the same fix for cut-off audio. A driver update writes the vendor values back, so check this tweak after you update audio or GPU drivers.")
                .Dev("Audio PerformanceIdleTime", Devices.AudioPowerKeys, "PerformanceIdleTime", RegistryValueKind.Binary, new byte[4], true)
                .Dev("Audio ConservationIdleTime", Devices.AudioPowerKeys, "ConservationIdleTime", RegistryValueKind.Binary, new byte[4], true)
                .Requires(() => Devices.AudioPowerKeys().Any(k => Reg.Get(Hive.HKLM, k, "PerformanceIdleTime") != null), "an audio driver with idle timers")
                .NeedsReboot();
            au.Off = "Idle power-down of the audio codec.";
            au.Breaks = "The audio device uses a little more power when no sound plays.";

            var rss = T("net_rss_place", N, "Network adapter: RSS on a fast core, two queues", "Moves the adapter's receive-side scaling (RSS) to start at logical processor 2 and use two queues. Network DPCs then run on a P-core that is not CPU 0, where many drivers run by default, and stay off the E-cores.")
                .Info("In valleyofdoom's xperf tests with game-like traffic, at most two RSS queues were in use. More queues only reserve more processors. If you pin a game to logical processors 2 and 3, set a different base processor in the adapter's Advanced properties.")
                .Requires(() => Devices.PhysicalNicKeys().Any() && HwProbe.Cpu.Threads >= 6, "a physical network adapter and 6 or more threads")
                .R(Risk.Moderate).NeedsReboot().Exp();
            rss.Off = "The driver's default RSS placement (starts at CPU 0).";
            rss.Breaks = "Very fast downloads (above 1 Gbit/s) can use more of the two RSS processors.";
            rss.Ops.Add(new NicAdvOp { Label = "RSS placement", Rules = new[] { new[] { "*RssBaseProcNumber", "=2" }, new[] { "*NumRssQueues", "=2" } } });

            var th = T("il_rawthrottle20", IL, "Background raw-mouse rate: 50 Hz (Windows 11)", "Windows 11 already limits how often BACKGROUND apps receive raw mouse input (about 125 Hz). This sets the interval to 20 ms (50 Hz), the highest value Windows accepts. Overlays, chat apps and RGB tools then use less CPU with a 4000 or 8000 Hz mouse. Your focused game always gets the full polling rate.")
                .Info("RawMouseThrottleDuration accepts 3 to 20 ms. The tweak 'Remove background raw-mouse throttle' does the opposite. If both are on, the throttle is off and this value does nothing.")
                .Dw(CU, @"Control Panel\Mouse", "RawMouseThrottleDuration", 20, null)
                .W11().NeedsRelogon();
            th.Off = "Nothing: background apps only get mouse input less often.";
            th.Breaks = "A background macro or recording tool that needs full-rate mouse input gets 50 Hz.";

            // E-cores as reserved CPU sets: only threads that ask for them run there. Mask = every logical processor that is not a P-core.
            var cpu = HwProbe.Cpu; ulong emask = cpu.Hybrid ? cpu.AllMask & ~cpu.FastMask : 0;
            var rs = T("exp_reserved_ecores", IL, "Kernel: reserve the E-cores (ReservedCpuSets)", "Marks every E-core as a reserved CPU set. Windows then puts no thread on an E-core unless the program asks for that core by affinity or CPU set. Games and their helper threads stay on the P-cores. The tweak does not move interrupts or DPCs.")
                .Info("valleyofdoom found this value in the Windows IoT real-time settings (SetRTCores). There are no published frame-time tests on hybrid CPUs, so compare 1 % lows with the A/B test page. Undo removes the value.")
                .Breaking("Everything runs on the P-cores only: renders, compiles, shader builds and background apps get slower, and the P-cores get busier.")
                .Bin(LM, KERNEL, "ReservedCpuSets", BitConverter.GetBytes(emask), null)
                .Requires(() => emask != 0, "a hybrid CPU with E-cores")
                .R(Risk.Advanced).NeedsReboot().Exp();
            rs.Off = "Normal use of the E-cores by Windows.";
        }

        static void AddV39()
        {
            string N = Cat.Network, TS = Cat.TasksServices;
            var itr = T("net_itr_off", N, "Network adapter: interrupt moderation off", "Sets interrupt moderation (and the ITR rate where the driver has it) to Off on every physical Ethernet / Wi-Fi adapter. The adapter interrupts the CPU for each packet instead of in small groups. Undo restores the driver's value.")
                .Info("Intel's own adapter guide says lower moderation raises CPU use, and to change it only after you verify a gain. In djdallmann's tests on an Intel adapter, Off gave about 4× more interrupts than Minimal. Measure ndis.sys DPC time on the Latency page before you keep it.")
                .Requires(() => Devices.PhysicalNicKeys().Any(), "a physical network adapter")
                .R(Risk.Moderate).NeedsReboot().Exp();
            itr.Off = "Interrupt moderation on the network adapter.";
            itr.Breaks = "Many more interrupts and more CPU load at high download speeds.";
            itr.Ops.Add(new NicAdvOp { Label = "interrupt moderation", Rules = new[] { new[] { "*InterruptModeration", "=0" }, new[] { "ITR", "=0" } } });

            var p = T("svc_plm_manual", TS, "Intel Platform License Manager: start only when needed", "Intel's management engine package installs this licensing service. On many PCs it times out after 45 s at every boot (event 7009). Manual start removes the timeout. Undo sets it back to Automatic.")
                .Requires(() => PlmSvcOp.Name != null, "the Intel Platform License Manager service");
            p.Off = "Automatic start of Intel's licensing service.";
            p.Breaks = "Software that uses Intel licensing (rare on gaming PCs) starts the service itself, a little later.";
            p.Ops.Add(new PlmSvcOp());
        }
    }
}
