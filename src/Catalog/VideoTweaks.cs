// ArchTweaker: Tweaks from the Stix Tweaks video cross-check ("10 Registry Tweaks for More FPS & Zero Delay", 3.44).
// Of its ten values, three were already in the catalog (prioritysep / Foreground boost, SystemResponsiveness,
// exp_serialtimer). PerfEnablePackageIdle = 1 is left out: the video says it keeps the CPU at full speed, but the
// name says it allows package idle, the opposite. The two unproven kernel values are Pro-only experiments.
using System;
using System.Collections.Generic;

namespace ArchTweaker
{
    public static partial class Catalog
    {
        static readonly Dictionary<string, object[]> GainsV44 = new Dictionary<string, object[]>
        {
            { "sys_sleepstudy", new object[] { 0, "No Sleep Study logging in the background. Negligible speed effect." } },
            { "rs_ioboost", new object[] { 1, "Unproven: less kernel bookkeeping when a thread waits on disk. Often within noise." } },
            { "priv_biometrics", new object[] { 0, "Privacy only: Windows stops using fingerprint and face data." } },
            { "wer_policy", new object[] { 0, "No crash-upload bursts. Negligible speed effect." } },
            { "exp_vsyncidle", new object[] { 1, "Unproven: reports range from steadier frame pacing to no change." } },
            { "exp_intsteer", new object[] { 1, "Unproven: can move interrupt work off busy cores, or onto the core the game uses." } },
        };

        static void AddV44()
        {
            string S = Cat.System, RS = Cat.Responsiveness, P = Cat.Privacy, GD = Cat.Gpu, IL = Cat.Input;
            const string KERNEL = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";

            T("sys_sleepstudy", S, "Turn off Sleep Study", "Windows stops recording the Sleep Study, a log of every sleep and wake of the PC. The autologger tweak stops only its trace session. This tweak turns the whole feature off.")
                .TurnsOff("Sleep Study recording.")
                .Breaking("The sleep report (powercfg /sleepstudy) and some battery troubleshooters.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "SleepStudyDisabled", 1, null)
                .NeedsReboot();

            T("rs_ioboost", RS, "Kernel: don't track IO priority boosts", "AlwaysTrackIoBoosting = 0: the kernel stops keeping track of the priority boost a thread gets while it waits on disk or other IO. Seen in gaming tweak packs. Not documented.")
                .TurnsOff("Kernel tracking of IO priority boosts.")
                .Info("Measure frame times on the A/B test page before keeping it.")
                .Breaking("Undocumented: the effect can differ between Windows builds.")
                .Dw(LM, KERNEL, "AlwaysTrackIoBoosting", 0, null)
                .R(Risk.Moderate).NeedsReboot();

            T("priv_biometrics", P, "Turn off biometrics (policy)", "Windows stops using fingerprint and face data. A policy turns off Windows Hello fingerprint and face sign-in.")
                .TurnsOff("Windows Hello fingerprint and face recognition.")
                .Breaking("Fingerprint and face sign-in. Sign in with a PIN or password instead.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Biometrics", "Enabled", 0, null)
                .R(Risk.Moderate);

            T("wer_policy", P, "Disable Windows Error Reporting (policy)", "The policy version of the Windows Error Reporting switch. Windows and apps read it first, and Windows Update doesn't reset it.")
                .TurnsOff("Windows Error Reporting.")
                .Breaking("Microsoft and app makers no longer receive your crash reports, so fixes for them may come later.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", 1, null)
                .In(PRESET_PRIVACY);

            T("exp_vsyncidle", GD, "Graphics: no VSync idle timeout", "VsyncIdleTimeout = 0: the graphics scheduler never stops the VSync interrupt when the screen is idle. Tweak guides claim steadier frame pacing. Not documented.")
                .TurnsOff("The idle timeout of the VSync interrupt.")
                .Info("Experimental. Compare frame times on the A/B test page before keeping it.")
                .Breaking("Undocumented: slightly more power use at idle, and on some systems no effect at all.")
                .Dw(LM, GD_KEY + @"\Scheduler", "VsyncIdleTimeout", 0, null)
                .R(Risk.Moderate).NeedsReboot().Exp();

            T("exp_intsteer", IL, "Kernel: interrupt steering mode 2", "InterruptSteeringFlags = 2 changes how Windows spreads device interrupts across CPU cores. Tweak guides use it to keep interrupts off the core the game runs on. Not documented.")
                .Info("Experimental. Measure DPC latency (Latency page) and frame times (A/B test) before keeping it. The Interrupt tuner sets per-device affinity in a documented way.")
                .TurnsOff("Windows' default way of spreading interrupts across cores.")
                .Breaking("Undocumented: on some systems interrupts land on fewer cores, which raises DPC latency and causes audio crackle.")
                .Dw(LM, KERNEL, "InterruptSteeringFlags", 2, null)
                .R(Risk.Advanced).NeedsReboot().Exp();
        }
    }
}
