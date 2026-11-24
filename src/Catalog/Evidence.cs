// ArchTweaker: Evidence for tweaks: sources and the curated evidence map.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ================= SOURCES & EVIDENCE =================
    public static class Evidence
    {
        // Returns { level, explanation, link or null }.
        public static string[] For(Tweak t)
        {
            var cur = EvidenceMap.For(t);   // 3.17: a named source wins over the general rule
            if (cur != null)
            {
                string first0 = t.Ops.OfType<VOp>().Select(o => o.Name).FirstOrDefault(n => !string.IsNullOrEmpty(n));
                return new[] { EvidenceMap.LongName(cur.L), cur.Source, cur.Url ?? (first0 == null ? null : "https://learn.microsoft.com/search/?terms=" + Uri.EscapeDataString(first0)) };
            }
            string gain = t.Gain ?? "";
            bool policy = t.Ops.OfType<VOp>().Any(o => (o.Path ?? "").IndexOf(@"\Policies\", StringComparison.OrdinalIgnoreCase) >= 0);
            bool numbers = System.Text.RegularExpressions.Regex.IsMatch(gain, @"\d\s*(%|ms|µs|fps|FPS|W\b)");
            string first = t.Ops.OfType<VOp>().Select(o => o.Name).FirstOrDefault(n => !string.IsNullOrEmpty(n));
            string search = first == null ? null : "https://learn.microsoft.com/search/?terms=" + Uri.EscapeDataString(first);
            if (t.Origin == "FR33THY Ultimate")
                return new[] { "Community guide", "Ported from the FR33THY Ultimate guide (MIT licence), re-implemented with exact undo.", "https://github.com/FR33THYFR33THY/Ultimate" };
            if (policy)
                return new[] { "Documented by Microsoft", "A Windows Group Policy setting. Microsoft documents what it does.", search };
            if (numbers)
                return new[] { "Measured in public tests", "The expected gain comes from public benchmarks and guides. Measure it on your PC with the Measure or A/B test page.", search };
            if (t.Impact == 0)
                return new[] { "Preference", "Changes how Windows behaves, not how fast it is.", search };
            return new[] { "Community knowledge", "Widely used in tuning guides. The effect is plausible but not well measured: use A/B test to check it on your PC.", search };
        }
    }

    // ================= CURATED EVIDENCE =================
    // Only tweaks with a source we can name. Everything else keeps the general rule in Evidence.For and gets no badge.
    public static class EvidenceMap
    {
        public enum Level { Documented, Measured, Unproven }
        public class Entry { public Level L; public string Source, Url; }
        const string ValleyOfDoom = "https://github.com/valleyofdoom/PC-Tuning", DjDallmann = "https://github.com/djdallmann/GamingPCSetup";
        static Entry D(string s, string u = null) { return new Entry { L = Level.Documented, Source = s, Url = u }; }
        static Entry M(string s, string u = null) { return new Entry { L = Level.Measured, Source = s, Url = u }; }
        static Entry U(string s) { return new Entry { L = Level.Unproven, Source = s }; }

        public static readonly Dictionary<string, Entry> Map = new Dictionary<string, Entry>
        {
            { "lastaccess", D("Microsoft fsutil documentation: turning off last-access updates makes file and folder access faster.", "https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-behavior") },
            { "8dot3", D("Microsoft fsutil documentation describes 8.3 short-name creation and how to turn it off.", "https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-8dot3name") },
            { "gd_nicpower", D("Microsoft's standard network driver keywords (*EEE and the wake and power keywords).", "https://learn.microsoft.com/en-us/windows-hardware/drivers/network/standardized-inf-keywords-for-power-management") },
            { "gd_audio_idle", D("Microsoft audio driver documentation: zero turns off idle detection. NVIDIA support article 5441 gives the same fix.", "https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/audio-device-class-inactivity-timer-implementation") },
            { "pp_hybrid", D("Microsoft documents the hybrid-CPU scheduling policies (SchedulingPolicy and ShortSchedulingPolicy).", "https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/configuration-for-hetero-power-scheduling-schedulingpolicy") },
            { "mmcss", D("Microsoft documents the Multimedia Class Scheduler Service and SystemResponsiveness.", "https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service") },
            { "netthrottle", D("Microsoft documents NetworkThrottlingIndex as part of the Multimedia Class Scheduler Service.", "https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service") },
            { "hags", D("Microsoft DirectX team: hardware-accelerated GPU scheduling.", "https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/") },
            { "windowedopt", D("Microsoft support: \"Optimizations for windowed games\" upgrades DirectX 10/11 games to the flip model.") },
            { "gamemode", D("Microsoft documents Game Mode in Windows Settings.") },
            { "powerthrottle", D("Microsoft documents power throttling (EcoQoS) for background processes.") },
            { "pp_parking", D("Microsoft documents the core-parking power settings (CPMINCORES and related).") },
            { "pp_epp", D("Microsoft documents the processor energy-performance preference power setting (PERFEPP).") },
            { "pp_boost", D("Microsoft documents the processor boost mode power setting (PERFBOOSTMODE).") },
            { "prioritysep", D("Microsoft documents Win32PrioritySeparation (foreground boost and quantum length).") },
            { "nagle", D("Microsoft documents TcpAckFrequency and TCPNoDelay for the TCP/IP stack.") },
            { "faststartup", D("Microsoft documents fast startup (hybrid shutdown, HiberbootEnabled).") },
            { "vbs", D("Microsoft documents virtualization-based security and memory integrity, and their gaming performance cost.") },
            { "sec_spectre", D("Microsoft documents FeatureSettingsOverride for the Spectre and Meltdown mitigations.") },
            { "gd_msi_more", D("Microsoft documents message-signaled interrupts (MSI) for device drivers.") },
            { "mpo", D("NVIDIA support recommends this registry value against flicker caused by multiplane overlay (MPO).") },
            { "il_rawthrottle", D("Microsoft Windows Developer blog (May 2023): background raw-input throttling. valleyofdoom measured it.", ValleyOfDoom) },
            { "il_rawthrottle20", D("Microsoft Windows Developer blog (May 2023): background raw-input throttling. valleyofdoom measured the interval.", ValleyOfDoom) },
            { "net_rss_place", M("valleyofdoom, xperf: at most two RSS queues in use with game traffic.", ValleyOfDoom) },
            { "gd_nv_p0", M("djdallmann: after a restart the GPU stayed at P0 with every NVIDIA power profile.", DjDallmann) },
            { "il_timerres", M("valleyofdoom measured timer resolution with MeasureSleep (Windows 11 and Server 2022 kernels).", ValleyOfDoom) },
            { "exp_serialtimer", U("Reports conflict: one tester saw timers spread in WinDbg, another saw no effect on their build.") },
            { "exp_reserved_ecores", U("valleyofdoom found the value. No published frame-time tests on hybrid CPUs.") },
            { "dz_nv_rmpower", U("No NVIDIA documentation. Tweak packs write it to five keys because nobody knows which one the driver reads.") },
            { "rs_ioboost", U("Seen in tweak packs (Stix Tweaks video, 2026). No Microsoft documentation and no published measurement.") },
            { "exp_vsyncidle", U("Seen in tweak packs. No Microsoft documentation and no published frame-time test.") },
            { "exp_intsteer", U("Seen in tweak packs. No Microsoft documentation. The Interrupt tuner sets device affinity in a documented way.") },
            { "il_queue", U("djdallmann: the value only sizes the input buffer. No latency measurement shows a gain.") },
        };
        public static Entry For(Tweak t) { Entry e; return t != null && Map.TryGetValue(t.Id, out e) ? e : null; }
        public static string Badge(Level l) { return l == Level.Documented ? "Documented" : l == Level.Measured ? "Measured" : "Unproven"; }
        public static string LongName(Level l) { return l == Level.Documented ? "Documented by Microsoft or the vendor" : l == Level.Measured ? "Measured by a named tester" : "Unproven"; }
    }
}
